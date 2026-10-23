using System.IO;
using System.Diagnostics;
using LinkView.Core.Settings;
using LinkView.Windows.Media;
using LinkView.Windows.Services;
using Microsoft.Extensions.Logging;

namespace LinkView.E2E;

/// <summary>
/// 端到端验证：两个独立的会话实例通过真实信令服务配对，
/// 主机采集真实桌面、VP8 编码并通过 WebRTC 发送，客机解码并统计帧数。
/// 需要先启动 LinkView.Signaling。
/// </summary>
internal static class Program
{
    private const string SignalingUri = "ws://127.0.0.1:5180/ws";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var root = Path.Combine(Path.GetTempPath(), "linkview-e2e-" + DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(root);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("LinkView.Windows.Services.SessionCoordinator", LogLevel.Debug);
        });

        var host = CreateParticipant("E2E-主机", Path.Combine(root, "host"), loggerFactory, autoAccept: true);
        var guest = CreateParticipant("E2E-客机", Path.Combine(root, "guest"), loggerFactory, autoAccept: false);

        var decodedFrames = 0;
        var lastDecodedAt = DateTime.MinValue;
        guest.Coordinator.RemoteFrame += (_, frame) =>
        {
            Interlocked.Increment(ref decodedFrames);
            lastDecodedAt = DateTime.UtcNow;
        };
        host.Coordinator.ErrorOccurred += (_, e) => Console.Error.WriteLine($"[host error] {e}");
        guest.Coordinator.ErrorOccurred += (_, e) => Console.Error.WriteLine($"[guest error] {e}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var pairing = await host.Coordinator.CreatePairingAsync(cts.Token);
            Console.WriteLine($"PAIRING CODE: {pairing.Code} (session {pairing.SessionId})");

            var joinTask = guest.Coordinator.JoinAsync(pairing.Code, cts.Token);
            await joinTask;
            Console.WriteLine("PAIR JOINED");

            await WaitForAsync(() => host.Coordinator.State == Core.Sessions.ShareSessionState.Connected &&
                                      guest.Coordinator.State == Core.Sessions.ShareSessionState.Connected,
                TimeSpan.FromSeconds(25), cts.Token, "WebRTC connected");
            Console.WriteLine("WEBRTC CONNECTED");

            var sources = ScreenCaptureService.EnumerateSources();
            var monitor = sources.FirstOrDefault(s => s.Kind == CaptureSourceKind.Monitor)
                          ?? throw new InvalidOperationException("没有可用的显示器采集源");
            Console.WriteLine($"CAPTURE SOURCE: {monitor.DisplayName} {monitor.Detail}");

            await host.Coordinator.StartSharingAsync(monitor, cts.Token);
            Console.WriteLine("SHARING STARTED");

            await WaitForAsync(() => Volatile.Read(ref decodedFrames) >= 10,
                TimeSpan.FromSeconds(25), cts.Token, "decoded frames");

            var hostStats = host.Coordinator.GetLiveStats();
            var guestStats = guest.Coordinator.GetLiveStats();
            Console.WriteLine($"HOST  sent={hostStats.SentFps}fps {hostStats.SentKbps:F0}kbps " +
                              $"resolve={hostStats.SendWidth}x{hostStats.SendHeight} codec={hostStats.Codec} " +
                              $"encode={hostStats.EncodeMs:F1}ms state={hostStats.State}");
            Console.WriteLine($"GUEST decoded={Volatile.Read(ref decodedFrames)} frames " +
                              $"recv={guestStats.ReceivedFps}fps {guestStats.ReceivedKbps:F0}kbps " +
                              $"resolve={guestStats.ReceiveWidth}x{guestStats.ReceiveHeight} " +
                              $"decode={guestStats.DecodeMs:F1}ms state={guestStats.State}");
            Console.WriteLine("RESULT: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RESULT: FAIL — " + ex.Message);
            Console.Error.WriteLine($"host state={host.Coordinator.State}, guest state={guest.Coordinator.State}, decoded={decodedFrames}");
            return 1;
        }
        finally
        {
            try { await host.Coordinator.DisconnectAsync("e2e done"); } catch (Exception) { }
            try { await guest.Coordinator.DisconnectAsync("e2e done"); } catch (Exception) { }
            await host.Coordinator.DisposeAsync();
            await guest.Coordinator.DisposeAsync();
        }
    }

    private sealed record Participant(SessionCoordinator Coordinator);

    private static Participant CreateParticipant(string name, string root, ILoggerFactory loggerFactory, bool autoAccept)
    {
        Directory.CreateDirectory(root);
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        var identity = new IdentityService(paths, loggerFactory.CreateLogger<IdentityService>());
        var settings = new SettingsService(paths, loggerFactory.CreateLogger<SettingsService>());
        var candidate = new AppSettings
        {
            General = new GeneralSettings { DeviceName = name, CloseToTray = false },
            Network = new NetworkSettings
            {
                SignalingServer = SignalingUri,
                StunServers = [],
                LanDiscoveryEnabled = false,
                ConnectionTimeoutSeconds = 20,
                IceTimeoutSeconds = 20,
                ReconnectMaxAttempts = 2
            },
            Connection = new ConnectionSettings
            {
                RequirePairingConfirmation = !autoAccept,
                AutoReconnect = true
            },
            Video = new VideoSettings
            {
                Preset = QualityPreset.Balanced,
                MaxWidth = 1280,
                MaxHeight = 720,
                TargetFps = 30,
                TargetBitrateKbps = 2500,
                MinBitrateKbps = 800,
                MaxBitrateKbps = 4000,
                KeyframeIntervalSeconds = 2
            },
            Diagnostics = new DiagnosticsSettings { LogLevel = AppLogLevel.Information }
        };
        if (!settings.Save(candidate, out var issues))
        {
            throw new InvalidOperationException("设置无效：" + string.Join("; ", issues.Select(i => i.Message)));
        }

        var capture = new ScreenCaptureService(loggerFactory.CreateLogger<ScreenCaptureService>());
        var coordinator = new SessionCoordinator(
            settings,
            identity,
            capture,
            new SecretStore(paths),
            new NetworkInfoService(),
            new ConnectionHistoryService(paths),
            new DeviceStore(paths),
            loggerFactory,
            loggerFactory.CreateLogger<SessionCoordinator>());
        return new Participant(coordinator);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct, string what)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"等待超时：{what}");
    }
}
