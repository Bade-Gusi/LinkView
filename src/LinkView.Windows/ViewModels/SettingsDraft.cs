using CommunityToolkit.Mvvm.ComponentModel;
using LinkView.Core.Settings;

namespace LinkView.Windows.ViewModels;

/// <summary>配置编辑草稿。只有点击“保存”后才写入磁盘并生效。</summary>
public partial class SettingsDraft : ObservableObject
{
    [ObservableProperty] private string deviceName = "";
    [ObservableProperty] private string defaultPage = "home";
    [ObservableProperty] private bool launchAtStartup;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool closeToTray = true;
    [ObservableProperty] private bool notificationsEnabled = true;

    [ObservableProperty] private QualityPreset preset = QualityPreset.Balanced;
    [ObservableProperty] private int maxWidth = 1920;
    [ObservableProperty] private int maxHeight = 1080;
    [ObservableProperty] private int targetFps = 30;
    [ObservableProperty] private int minFps = 10;
    [ObservableProperty] private int targetBitrateKbps = 4000;
    [ObservableProperty] private int minBitrateKbps = 1000;
    [ObservableProperty] private int maxBitrateKbps = 8000;
    [ObservableProperty] private VideoCodecPreference codec = VideoCodecPreference.Auto;
    [ObservableProperty] private int keyframeIntervalSeconds = 2;
    [ObservableProperty] private bool adaptiveBitrate = true;
    [ObservableProperty] private bool adaptiveFrameRate = true;
    [ObservableProperty] private bool preferHardwareEncode = true;
    [ObservableProperty] private bool preferHardwareDecode = true;
    [ObservableProperty] private bool allowSoftwareFallback = true;
    [ObservableProperty] private ScaleMode scaleMode = ScaleMode.Fit;

    [ObservableProperty] private NetworkPreference networkPreference = NetworkPreference.Auto;
    [ObservableProperty] private string stunServersText = "";
    [ObservableProperty] private string turnUri = "";
    [ObservableProperty] private string turnUsername = "";
    [ObservableProperty] private string turnCredential = "";
    [ObservableProperty] private bool clearTurnCredential;
    [ObservableProperty] private IceTransportMode iceTransport = IceTransportMode.All;
    [ObservableProperty] private int connectionTimeoutSeconds = 30;
    [ObservableProperty] private int iceTimeoutSeconds = 20;
    [ObservableProperty] private int reconnectMaxAttempts = 4;
    [ObservableProperty] private int reconnectInitialDelayMs = 1000;
    [ObservableProperty] private int reconnectMaxDelayMs = 8000;
    [ObservableProperty] private bool lanDiscoveryEnabled = true;
    [ObservableProperty] private bool allowCrossNetwork = true;
    [ObservableProperty] private string signalingServer = "";

    [ObservableProperty] private bool requirePairingConfirmation = true;
    [ObservableProperty] private bool allowUnknownDeviceRequests;
    [ObservableProperty] private bool autoReconnect = true;
    [ObservableProperty] private int sessionTimeoutSeconds = 900;

    [ObservableProperty] private int pairingCodeTtlSeconds = 300;
    [ObservableProperty] private bool allowClipboardSharing;
    [ObservableProperty] private bool deleteSessionStateOnEnd = true;
    [ObservableProperty] private bool showEncryptionStatus = true;

    [ObservableProperty] private bool maskSensitiveLogs = true;
    [ObservableProperty] private int historyRetentionDays = 30;
    [ObservableProperty] private int recentDeviceLimit = 8;

    [ObservableProperty] private ThemeMode theme = ThemeMode.System;
    [ObservableProperty] private string accentColor = "#3B82F6";
    [ObservableProperty] private AnimationLevel animations = AnimationLevel.Full;
    [ObservableProperty] private InterfaceDensity density = InterfaceDensity.Comfortable;
    [ObservableProperty] private double fontScale = 1.0;
    [ObservableProperty] private string language = "zh-CN";

    [ObservableProperty] private int statsRefreshMs = 1000;
    [ObservableProperty] private bool hardwareEncode = true;
    [ObservableProperty] private bool hardwareDecode = true;
    [ObservableProperty] private bool softwareFallback = true;
    [ObservableProperty] private bool renderVSync = true;

    [ObservableProperty] private AppLogLevel logLevel = AppLogLevel.Information;
    [ObservableProperty] private int logRetentionDays = 7;
    [ObservableProperty] private bool captureMediaTimings = true;
    [ObservableProperty] private bool exportRedactedOnly = true;

    [ObservableProperty] private bool useWindowsGraphicsCapture = true;
    [ObservableProperty] private bool enableDataChannelControl = true;

    public void Load(AppSettings s)
    {
        DeviceName = s.General.DeviceName;
        DefaultPage = s.General.DefaultPage;
        LaunchAtStartup = s.General.LaunchAtStartup;
        StartMinimized = s.General.StartMinimized;
        CloseToTray = s.General.CloseToTray;
        NotificationsEnabled = s.General.NotificationsEnabled;

        Preset = s.Video.Preset;
        MaxWidth = s.Video.MaxWidth;
        MaxHeight = s.Video.MaxHeight;
        TargetFps = s.Video.TargetFps;
        MinFps = s.Video.MinFps;
        TargetBitrateKbps = s.Video.TargetBitrateKbps;
        MinBitrateKbps = s.Video.MinBitrateKbps;
        MaxBitrateKbps = s.Video.MaxBitrateKbps;
        Codec = s.Video.Codec;
        KeyframeIntervalSeconds = s.Video.KeyframeIntervalSeconds;
        AdaptiveBitrate = s.Video.AdaptiveBitrate;
        AdaptiveFrameRate = s.Video.AdaptiveFrameRate;
        PreferHardwareEncode = s.Video.PreferHardwareEncode;
        PreferHardwareDecode = s.Video.PreferHardwareDecode;
        AllowSoftwareFallback = s.Video.AllowSoftwareFallback;
        ScaleMode = s.Video.ScaleMode;

        NetworkPreference = s.Network.Preference;
        StunServersText = string.Join(Environment.NewLine, s.Network.StunServers);
        var turn = s.Network.TurnServers.FirstOrDefault();
        TurnUri = turn?.Uri ?? "";
        TurnUsername = turn?.Username ?? "";
        TurnCredential = "";
        ClearTurnCredential = false;
        IceTransport = s.Network.IceTransport;
        ConnectionTimeoutSeconds = s.Network.ConnectionTimeoutSeconds;
        IceTimeoutSeconds = s.Network.IceTimeoutSeconds;
        ReconnectMaxAttempts = s.Network.ReconnectMaxAttempts;
        ReconnectInitialDelayMs = s.Network.ReconnectInitialDelayMs;
        ReconnectMaxDelayMs = s.Network.ReconnectMaxDelayMs;
        LanDiscoveryEnabled = s.Network.LanDiscoveryEnabled;
        AllowCrossNetwork = s.Network.AllowCrossNetwork;
        SignalingServer = s.Network.SignalingServer;

        RequirePairingConfirmation = s.Connection.RequirePairingConfirmation;
        AllowUnknownDeviceRequests = s.Connection.AllowUnknownDeviceRequests;
        AutoReconnect = s.Connection.AutoReconnect;
        SessionTimeoutSeconds = s.Connection.SessionTimeoutSeconds;

        PairingCodeTtlSeconds = s.Security.PairingCodeTtlSeconds;
        AllowClipboardSharing = s.Security.AllowClipboardSharing;
        DeleteSessionStateOnEnd = s.Security.DeleteSessionStateOnEnd;
        ShowEncryptionStatus = s.Security.ShowEncryptionStatus;

        MaskSensitiveLogs = s.Privacy.MaskSensitiveLogs;
        HistoryRetentionDays = s.Privacy.HistoryRetentionDays;
        RecentDeviceLimit = s.Privacy.RecentDeviceLimit;

        Theme = s.Appearance.Theme;
        AccentColor = s.Appearance.AccentColor;
        Animations = s.Appearance.Animations;
        Density = s.Appearance.Density;
        FontScale = s.Appearance.FontScale;
        Language = s.Appearance.Language;

        StatsRefreshMs = s.Performance.StatsRefreshMs;
        HardwareEncode = s.Performance.HardwareEncode;
        HardwareDecode = s.Performance.HardwareDecode;
        SoftwareFallback = s.Performance.SoftwareFallback;
        RenderVSync = s.Performance.RenderVSync;

        LogLevel = s.Diagnostics.LogLevel;
        LogRetentionDays = s.Diagnostics.LogRetentionDays;
        CaptureMediaTimings = s.Diagnostics.CaptureMediaTimings;
        ExportRedactedOnly = s.Diagnostics.ExportRedactedOnly;

        UseWindowsGraphicsCapture = s.Experimental.UseWindowsGraphicsCapture;
        EnableDataChannelControl = s.Experimental.EnableDataChannelControl;
    }

    public AppSettings ToSettings(AppSettings original)
    {
        var stun = StunServersText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var turnServers = new List<TurnServerSettings>();
        if (!string.IsNullOrWhiteSpace(TurnUri))
        {
            turnServers.Add(new TurnServerSettings { Uri = TurnUri.Trim(), Username = TurnUsername.Trim() });
        }

        return original with
        {
            General = new GeneralSettings
            {
                DeviceName = DeviceName.Trim(),
                DefaultPage = DefaultPage,
                LaunchAtStartup = LaunchAtStartup,
                StartMinimized = StartMinimized,
                CloseToTray = CloseToTray,
                NotificationsEnabled = NotificationsEnabled,
                NotifyOnConnect = original.General.NotifyOnConnect
            },
            Video = new VideoSettings
            {
                Preset = Preset,
                MaxWidth = MaxWidth,
                MaxHeight = MaxHeight,
                TargetFps = TargetFps,
                MinFps = MinFps,
                TargetBitrateKbps = TargetBitrateKbps,
                MinBitrateKbps = MinBitrateKbps,
                MaxBitrateKbps = MaxBitrateKbps,
                Codec = Codec,
                KeyframeIntervalSeconds = KeyframeIntervalSeconds,
                AdaptiveBitrate = AdaptiveBitrate,
                AdaptiveFrameRate = AdaptiveFrameRate,
                PreferHardwareEncode = PreferHardwareEncode,
                PreferHardwareDecode = PreferHardwareDecode,
                AllowSoftwareFallback = AllowSoftwareFallback,
                ScaleMode = ScaleMode
            },
            Network = new NetworkSettings
            {
                Preference = NetworkPreference,
                StunServers = stun,
                TurnServers = turnServers,
                IceTransport = IceTransport,
                ConnectionTimeoutSeconds = ConnectionTimeoutSeconds,
                IceTimeoutSeconds = IceTimeoutSeconds,
                ReconnectMaxAttempts = ReconnectMaxAttempts,
                ReconnectInitialDelayMs = ReconnectInitialDelayMs,
                ReconnectMaxDelayMs = ReconnectMaxDelayMs,
                LanDiscoveryEnabled = LanDiscoveryEnabled,
                AllowCrossNetwork = AllowCrossNetwork,
                PreferredInterfaceId = original.Network.PreferredInterfaceId,
                SignalingServer = SignalingServer.Trim()
            },
            Connection = new ConnectionSettings
            {
                RequirePairingConfirmation = RequirePairingConfirmation,
                AllowUnknownDeviceRequests = AllowUnknownDeviceRequests,
                SessionTimeoutSeconds = SessionTimeoutSeconds,
                AutoReconnect = AutoReconnect
            },
            Security = new SecuritySettings
            {
                PairingCodeTtlSeconds = PairingCodeTtlSeconds,
                AllowClipboardSharing = AllowClipboardSharing,
                DeleteSessionStateOnEnd = DeleteSessionStateOnEnd,
                ShowEncryptionStatus = ShowEncryptionStatus
            },
            Privacy = new PrivacySettings
            {
                NeverRecordScreen = true,
                MaskSensitiveLogs = MaskSensitiveLogs,
                HistoryRetentionDays = HistoryRetentionDays,
                RecentDeviceLimit = RecentDeviceLimit
            },
            Appearance = new AppearanceSettings
            {
                Theme = Theme,
                AccentColor = AccentColor,
                Animations = Animations,
                Density = Density,
                FontScale = FontScale,
                Language = Language
            },
            Performance = new PerformanceSettings
            {
                StatsRefreshMs = StatsRefreshMs,
                HardwareEncode = HardwareEncode,
                HardwareDecode = HardwareDecode,
                SoftwareFallback = SoftwareFallback,
                RenderVSync = RenderVSync
            },
            Diagnostics = new DiagnosticsSettings
            {
                LogLevel = LogLevel,
                LogRetentionDays = LogRetentionDays,
                CaptureMediaTimings = CaptureMediaTimings,
                ExportRedactedOnly = ExportRedactedOnly
            },
            Experimental = new ExperimentalSettings
            {
                UseWindowsGraphicsCapture = UseWindowsGraphicsCapture,
                EnableDataChannelControl = EnableDataChannelControl
            }
        };
    }
}
