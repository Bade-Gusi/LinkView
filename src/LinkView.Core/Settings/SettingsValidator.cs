using System.Text.RegularExpressions;

namespace LinkView.Core.Settings;

public enum SettingsIssueSeverity { Info, Warning, Error }

public sealed record SettingsIssue(string Field, string Message, SettingsIssueSeverity Severity);

public static partial class SettingsValidator
{
    public static IReadOnlyList<SettingsIssue> Validate(AppSettings settings)
    {
        var issues = new List<SettingsIssue>();
        var video = settings.Video;
        var network = settings.Network;

        if (string.IsNullOrWhiteSpace(settings.General.DeviceName))
            issues.Add(new("General.DeviceName", "设备名称不能为空", SettingsIssueSeverity.Error));
        else if (settings.General.DeviceName.Length > 64)
            issues.Add(new("General.DeviceName", "设备名称不能超过 64 个字符", SettingsIssueSeverity.Error));

        if (video.MaxWidth is < 320 or > 7680)
            issues.Add(new("Video.MaxWidth", "宽度必须在 320 到 7680 之间", SettingsIssueSeverity.Error));
        if (video.MaxHeight is < 240 or > 4320)
            issues.Add(new("Video.MaxHeight", "高度必须在 240 到 4320 之间", SettingsIssueSeverity.Error));
        if (video.TargetFps is < 5 or > 120)
            issues.Add(new("Video.TargetFps", "目标帧率必须在 5 到 120 之间", SettingsIssueSeverity.Error));
        if (video.MinFps < 1 || video.MinFps > video.TargetFps)
            issues.Add(new("Video.MinFps", "最低帧率必须大于 0 且不超过目标帧率", SettingsIssueSeverity.Error));
        if (video.MinBitrateKbps < 100)
            issues.Add(new("Video.MinBitrateKbps", "最低码率不能低于 100 kbps", SettingsIssueSeverity.Error));
        if (video.MinBitrateKbps > video.TargetBitrateKbps)
            issues.Add(new("Video.TargetBitrateKbps", "目标码率不能低于最低码率", SettingsIssueSeverity.Error));
        if (video.TargetBitrateKbps > video.MaxBitrateKbps)
            issues.Add(new("Video.MaxBitrateKbps", "最高码率不能低于目标码率", SettingsIssueSeverity.Error));
        if (video.KeyframeIntervalSeconds is < 1 or > 10)
            issues.Add(new("Video.KeyframeIntervalSeconds", "关键帧间隔必须在 1 到 10 秒之间", SettingsIssueSeverity.Error));

        if (network.ConnectionTimeoutSeconds is < 5 or > 180)
            issues.Add(new("Network.ConnectionTimeoutSeconds", "连接超时必须在 5 到 180 秒之间", SettingsIssueSeverity.Error));
        if (network.IceTimeoutSeconds is < 5 or > 120)
            issues.Add(new("Network.IceTimeoutSeconds", "ICE 超时必须在 5 到 120 秒之间", SettingsIssueSeverity.Error));
        if (network.ReconnectMaxAttempts is < 0 or > 20)
            issues.Add(new("Network.ReconnectMaxAttempts", "重连次数必须在 0 到 20 之间", SettingsIssueSeverity.Error));
        if (network.ReconnectInitialDelayMs < 250 || network.ReconnectInitialDelayMs > network.ReconnectMaxDelayMs)
            issues.Add(new("Network.ReconnectInitialDelayMs", "初始重连延迟必须不小于 250ms 且不超过最大延迟", SettingsIssueSeverity.Error));

        foreach (var server in network.StunServers)
        {
            if (!IsValidUri(server, "stun"))
                issues.Add(new("Network.StunServers", $"无效的 STUN 地址：{server}", SettingsIssueSeverity.Error));
        }

        foreach (var server in network.TurnServers)
        {
            if (!IsValidUri(server.Uri, "turn"))
                issues.Add(new("Network.TurnServers", $"无效的 TURN 地址：{server.Uri}", SettingsIssueSeverity.Error));
            if (string.IsNullOrWhiteSpace(server.Username))
                issues.Add(new("Network.TurnServers", "TURN 服务器需要用户名", SettingsIssueSeverity.Warning));
        }

        if (network.IceTransport == IceTransportMode.RelayOnly && network.TurnServers.Count == 0)
            issues.Add(new("Network.IceTransport", "仅中继模式必须配置至少一个 TURN 服务器", SettingsIssueSeverity.Error));

        if (!string.IsNullOrWhiteSpace(network.SignalingServer) && !IsValidUri(network.SignalingServer, null, allowWs: true))
            issues.Add(new("Network.SignalingServer", "信令服务地址必须以 ws:// 或 wss:// 开头", SettingsIssueSeverity.Error));

        if (settings.Security.PairingCodeTtlSeconds is < 60 or > 3600)
            issues.Add(new("Security.PairingCodeTtlSeconds", "配对码有效期必须在 60 到 3600 秒之间", SettingsIssueSeverity.Error));

        if (!HexColorRegex().IsMatch(settings.Appearance.AccentColor))
            issues.Add(new("Appearance.AccentColor", "强调色必须是 #RRGGBB 格式", SettingsIssueSeverity.Error));

        if (settings.Appearance.FontScale is < 0.8 or > 1.6)
            issues.Add(new("Appearance.FontScale", "字体缩放必须在 0.8 到 1.6 之间", SettingsIssueSeverity.Error));

        if (settings.Privacy.HistoryRetentionDays is < 0 or > 3650)
            issues.Add(new("Privacy.HistoryRetentionDays", "历史保留天数必须在 0 到 3650 之间", SettingsIssueSeverity.Error));

        if (settings.Performance.StatsRefreshMs is < 250 or > 10000)
            issues.Add(new("Performance.StatsRefreshMs", "统计刷新间隔必须在 250ms 到 10s 之间", SettingsIssueSeverity.Error));

        if (settings.Network.Preference == NetworkPreference.Ipv6Only)
            issues.Add(new("Network.Preference", "IPv6-only 模式在仅有 IPv4 的网络中会连接失败，请确认网络支持。", SettingsIssueSeverity.Warning));

        return issues;
    }

    public static bool IsValid(AppSettings settings) =>
        Validate(settings).All(i => i.Severity != SettingsIssueSeverity.Error);

    private static bool IsValidUri(string value, string? expectedScheme, bool allowWs = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (allowWs)
        {
            return uri.Scheme is "ws" or "wss" && !string.IsNullOrWhiteSpace(uri.Host);
        }

        return expectedScheme is null || uri.Scheme.Equals(expectedScheme, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();
}
