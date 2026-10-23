using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LinkView.Core.Networking;
using LinkView.Core.Settings;
using LinkView.Protocol;
using LinkView.Windows.Services.Logging;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services;

public enum CheckStatus
{
    Passed,
    Warning,
    Failed,
    Skipped,
    Running
}

public sealed record DiagnosticCheck(
    string Name,
    string Category,
    CheckStatus Status,
    string Detail,
    long DurationMs = 0);

public sealed class DiagnosticsService
{
    private readonly SettingsService _settings;
    private readonly NetworkInfoService _networkInfo;
    private readonly StunProbe _stun;
    private readonly SessionCoordinator _session;
    private readonly AppPaths _paths;
    private readonly LogStore _logs;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(
        SettingsService settings,
        NetworkInfoService networkInfo,
        StunProbe stun,
        SessionCoordinator session,
        AppPaths paths,
        LogStore logs,
        ILogger<DiagnosticsService> logger)
    {
        _settings = settings;
        _networkInfo = networkInfo;
        _stun = stun;
        _session = session;
        _paths = paths;
        _logs = logs;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(
        IProgress<DiagnosticCheck>? progress,
        CancellationToken ct)
    {
        var results = new List<DiagnosticCheck>();
        var settings = _settings.Current;

        void Add(DiagnosticCheck check)
        {
            results.Add(check);
            progress?.Report(check);
        }

        var sw = Stopwatch.StartNew();
        var addresses = _networkInfo.GetAddresses();
        var v4 = addresses.Where(a => a.Family == IpFamily.IPv4 && a.IsUsableForConnection).ToArray();
        Add(new DiagnosticCheck(
            "IPv4 地址",
            "本机网络",
            v4.Length > 0 ? CheckStatus.Passed : CheckStatus.Failed,
            v4.Length > 0
                ? string.Join(" · ", v4.Select(a => $"{a.Address} ({a.InterfaceName})"))
                : "未找到可用的 IPv4 地址",
            sw.ElapsedMilliseconds));

        sw.Restart();
        var v6 = addresses.Where(a => a.Family == IpFamily.IPv6 && a.IsUsableForConnection).ToArray();
        Add(new DiagnosticCheck(
            "IPv6 地址",
            "本机网络",
            v6.Length > 0 ? CheckStatus.Passed : CheckStatus.Warning,
            v6.Length > 0
                ? string.Join(" · ", v6.Select(a => $"{a.Address} ({ScopeLabel(a.Scope)})"))
                : "未找到全局 IPv6 地址；这不影响 IPv4 连接",
            sw.ElapsedMilliseconds));

        sw.Restart();
        var linkLocalCount = addresses.Count(a => a.Scope == IpScope.LinkLocal);
        Add(new DiagnosticCheck(
            "接口枚举",
            "本机网络",
            CheckStatus.Passed,
            $"{addresses.Select(a => a.InterfaceId).Distinct().Count()} 个接口，{addresses.Count} 个地址" +
            (linkLocalCount > 0 ? $"（其中 {linkLocalCount} 个链路本地地址不会被当作公网地址）" : ""),
            sw.ElapsedMilliseconds));

        if (!string.IsNullOrWhiteSpace(settings.Network.SignalingServer))
        {
            sw.Restart();
            var signaling = await CheckSignalingAsync(settings.Network.SignalingServer, ct).ConfigureAwait(false);
            Add(signaling with { DurationMs = sw.ElapsedMilliseconds });
        }

        foreach (var stunServer in settings.Network.StunServers.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            sw.Restart();
            var result = await _stun.ProbeAsync(stunServer, ct).ConfigureAwait(false);
            Add(new DiagnosticCheck(
                "STUN：" + stunServer,
                "NAT 穿透",
                result.Success ? CheckStatus.Passed : CheckStatus.Failed,
                result.Success
                    ? $"RTT {result.RoundTripMs} ms；观测到映射地址 {result.MappedAddress ?? "未返回"}"
                    : result.Error ?? "不可达",
                sw.ElapsedMilliseconds));
        }

        sw.Restart();
        if (settings.Network.TurnServers.Count == 0)
        {
            Add(new DiagnosticCheck(
                "TURN 配置",
                "NAT 穿透",
                CheckStatus.Warning,
                "未配置 TURN。对称 NAT 或严格防火墙环境下可能无法跨网络连接。",
                sw.ElapsedMilliseconds));
        }
        else
        {
            foreach (var turn in settings.Network.TurnServers)
            {
                var hasCredential = !string.IsNullOrWhiteSpace(turn.Username);
                Add(new DiagnosticCheck(
                    "TURN：" + turn.Uri,
                    "NAT 穿透",
                    hasCredential ? CheckStatus.Passed : CheckStatus.Warning,
                    hasCredential ? "用户名已配置（凭据保存在系统安全存储中）" : "缺少 TURN 用户名，无法分配中继",
                    sw.ElapsedMilliseconds));
            }
        }

        sw.Restart();
        var stats = _session.GetLiveStats();
        Add(new DiagnosticCheck(
            "当前会话",
            "实时连接",
            _session.State is Core.Sessions.ShareSessionState.Connected or Core.Sessions.ShareSessionState.Sharing
                ? CheckStatus.Passed
                : CheckStatus.Skipped,
            _session.State is Core.Sessions.ShareSessionState.Connected or Core.Sessions.ShareSessionState.Sharing
                ? $"已连接 {stats.PeerName}；连接状态 {stats.ConnectionState}；ICE {stats.IceState}；路径 {stats.PathLabel}"
                : "当前没有活动会话",
            sw.ElapsedMilliseconds));

        return results;
    }

    private async Task<DiagnosticCheck> CheckSignalingAsync(string serverUri, CancellationToken ct)
    {
        if (!Uri.TryCreate(serverUri, UriKind.Absolute, out var wsUri))
        {
            return new DiagnosticCheck("信令服务", "信令", CheckStatus.Failed, "地址格式非法");
        }

        var httpScheme = wsUri.Scheme == "wss" ? "https" : "http";
        var healthUri = new UriBuilder(httpScheme, wsUri.Host, wsUri.Port, "/healthz").Uri;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(healthUri, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new DiagnosticCheck("信令服务", "信令", CheckStatus.Failed,
                    $"HTTP {(int)response.StatusCode}：{Truncate(text, 160)}");
            }

            using var doc = JsonDocument.Parse(text);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            return new DiagnosticCheck("信令服务", "信令",
                status == "ok" ? CheckStatus.Passed : CheckStatus.Warning,
                $"可达（{wsUri.Authority}）" + (status is null ? "" : $"，状态 {status}"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new DiagnosticCheck("信令服务", "信令", CheckStatus.Failed,
                $"无法访问 {healthUri}：{ex.Message}");
        }
    }

    private static string ScopeLabel(IpScope scope) => scope switch
    {
        IpScope.GlobalUnicast => "全局",
        IpScope.Private => "内网/ULA",
        IpScope.LinkLocal => "链路本地",
        _ => scope.ToString()
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    public string ExportReport()
    {
        _paths.EnsureCreated();
        var settings = _settings.Current;
        var stats = _session.GetLiveStats();
        var sb = new StringBuilder();
        sb.AppendLine("# LinkView 诊断报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"- 应用版本：{typeof(DiagnosticsService).Assembly.GetName().Version}");
        sb.AppendLine($"- 运行时：{Environment.Version}");
        sb.AppendLine($"- 操作系统：{Environment.OSVersion}");
        sb.AppendLine($"- 设备 ID：{MaskId(_session.GetType().Assembly.GetName().Name)}");
        sb.AppendLine();
        sb.AppendLine("## 会话状态");
        sb.AppendLine($"- 状态：{_session.State}");
        sb.AppendLine($"- 对端：{stats.PeerName ?? "无"}");
        sb.AppendLine($"- 连接状态：{stats.ConnectionState} / ICE {stats.IceState}");
        sb.AppendLine($"- 路径：{stats.PathLabel}");
        sb.AppendLine($"- 发送：{stats.SentFps} fps，{stats.SentKbps:F0} kbps，{stats.EncodeMs:F1} ms/帧");
        sb.AppendLine($"- 接收：{stats.ReceivedFps} fps，{stats.ReceivedKbps:F0} kbps，{stats.DecodeMs:F1} ms/帧");
        sb.AppendLine($"- 分辨率：发送 {stats.SendWidth}×{stats.SendHeight}，接收 {stats.ReceiveWidth}×{stats.ReceiveHeight}");
        sb.AppendLine();
        sb.AppendLine("## 网络接口");
        foreach (var a in _networkInfo.GetAddresses())
        {
            sb.AppendLine($"- {a.InterfaceName} [{a.Family}] {a.Address} ({a.Scope}) {(a.IsUsableForConnection ? "可用" : "不可用于连接")}");
        }

        sb.AppendLine();
        sb.AppendLine("## 配置摘要（不含凭据）");
        sb.AppendLine($"- 网络偏好：{settings.Network.Preference}");
        sb.AppendLine($"- ICE 策略：{settings.Network.IceTransport}");
        sb.AppendLine($"- STUN：{string.Join(", ", settings.Network.StunServers.DefaultIfEmpty("未配置"))}");
        sb.AppendLine($"- TURN 数量：{settings.Network.TurnServers.Count}");
        sb.AppendLine($"- 信令服务：{settings.Network.SignalingServer}");
        sb.AppendLine($"- 视频：{settings.Video.MaxWidth}×{settings.Video.MaxHeight} @ {settings.Video.TargetFps} fps，" +
                      $"{settings.Video.TargetBitrateKbps} kbps，VP8");
        sb.AppendLine();
        sb.AppendLine("## 最近日志（已脱敏）");
        foreach (var log in _logs.Snapshot().TakeLast(200))
        {
            sb.AppendLine($"{log.Timestamp:HH:mm:ss.fff} {log.LevelLabel} {log.CategoryShort} | {log.Message}");
        }

        var path = Path.Combine(_paths.DiagnosticsDirectory,
            $"linkview-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("diagnostics_exported {Path}", path);
        return path;
    }

    private static string MaskId(string? value) => string.IsNullOrEmpty(value) ? "unknown" : value;
}
