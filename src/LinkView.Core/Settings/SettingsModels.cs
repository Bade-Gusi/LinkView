using System.Text.Json.Serialization;

namespace LinkView.Core.Settings;

public enum ThemeMode { System, Light, Dark }
public enum NetworkPreference { Auto, Ipv4Preferred, Ipv6Preferred, Ipv4Only, Ipv6Only, LanOnly }
public enum QualityPreset { Auto, Smooth, Balanced, High, Ultra, Custom }
public enum VideoCodecPreference { Auto, Vp8, H264 }
public enum ScaleMode { Fit, Fill, Original }
public enum AnimationLevel { Full, Reduced, None }
public enum InterfaceDensity { Comfortable, Compact }
public enum IceTransportMode { All, RelayOnly }
public enum AppLogLevel { Trace, Debug, Information, Warning, Error }

public sealed record VideoSettings
{
    public QualityPreset Preset { get; init; } = QualityPreset.Balanced;
    public int MaxWidth { get; init; } = 1920;
    public int MaxHeight { get; init; } = 1080;
    public int TargetFps { get; init; } = 30;
    public int MinFps { get; init; } = 10;
    public int TargetBitrateKbps { get; init; } = 4000;
    public int MinBitrateKbps { get; init; } = 1000;
    public int MaxBitrateKbps { get; init; } = 8000;
    public VideoCodecPreference Codec { get; init; } = VideoCodecPreference.Auto;
    public int KeyframeIntervalSeconds { get; init; } = 2;
    public bool AdaptiveBitrate { get; init; } = true;
    public bool AdaptiveFrameRate { get; init; } = true;
    public bool PreferHardwareEncode { get; init; } = true;
    public bool PreferHardwareDecode { get; init; } = true;
    public bool AllowSoftwareFallback { get; init; } = true;
    public ScaleMode ScaleMode { get; init; } = ScaleMode.Fit;
}

public sealed record TurnServerSettings
{
    public string Uri { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    /// <summary>凭据不写入普通配置文件，只保存在操作系统安全存储中。</summary>
    [JsonIgnore]
    public string? Credential { get; init; }
}

public sealed record NetworkSettings
{
    public NetworkPreference Preference { get; init; } = NetworkPreference.Auto;
    public List<string> StunServers { get; init; } = [];
    public List<TurnServerSettings> TurnServers { get; init; } = [];
    public IceTransportMode IceTransport { get; init; } = IceTransportMode.All;
    public int ConnectionTimeoutSeconds { get; init; } = 30;
    public int IceTimeoutSeconds { get; init; } = 20;
    public int ReconnectMaxAttempts { get; init; } = 4;
    public int ReconnectInitialDelayMs { get; init; } = 1000;
    public int ReconnectMaxDelayMs { get; init; } = 8000;
    public bool LanDiscoveryEnabled { get; init; } = true;
    public bool AllowCrossNetwork { get; init; } = true;
    public string? PreferredInterfaceId { get; init; }
    public string SignalingServer { get; init; } = "ws://127.0.0.1:5180/ws";
}

public sealed record ConnectionSettings
{
    public bool RequirePairingConfirmation { get; init; } = true;
    public bool AllowUnknownDeviceRequests { get; init; }
    public int SessionTimeoutSeconds { get; init; } = 900;
    public bool AutoReconnect { get; init; } = true;
}

public sealed record SecuritySettings
{
    public int PairingCodeTtlSeconds { get; init; } = 300;
    public bool AllowClipboardSharing { get; init; }
    public bool DeleteSessionStateOnEnd { get; init; } = true;
    public bool ShowEncryptionStatus { get; init; } = true;
}

public sealed record PrivacySettings
{
    public bool NeverRecordScreen { get; init; } = true;
    public bool MaskSensitiveLogs { get; init; } = true;
    public int HistoryRetentionDays { get; init; } = 30;
    public int RecentDeviceLimit { get; init; } = 8;
}

public sealed record AppearanceSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;
    public string AccentColor { get; init; } = "#3B82F6";
    public AnimationLevel Animations { get; init; } = AnimationLevel.Full;
    public InterfaceDensity Density { get; init; } = InterfaceDensity.Comfortable;
    public double FontScale { get; init; } = 1.0;
    public string Language { get; init; } = "zh-CN";
}

public sealed record GeneralSettings
{
    public string DeviceName { get; init; } = Environment.MachineName;
    public string DefaultPage { get; init; } = "home";
    public bool LaunchAtStartup { get; init; }
    public bool StartMinimized { get; init; }
    public bool CloseToTray { get; init; } = true;
    public bool NotificationsEnabled { get; init; } = true;
    public bool NotifyOnConnect { get; init; } = true;
}

public sealed record PerformanceSettings
{
    public int StatsRefreshMs { get; init; } = 1000;
    public bool HardwareEncode { get; init; } = true;
    public bool HardwareDecode { get; init; } = true;
    public bool SoftwareFallback { get; init; } = true;
    public bool RenderVSync { get; init; } = true;
}

public sealed record DiagnosticsSettings
{
    public AppLogLevel LogLevel { get; init; } = AppLogLevel.Information;
    public int LogRetentionDays { get; init; } = 7;
    public bool CaptureMediaTimings { get; init; } = true;
    public bool ExportRedactedOnly { get; init; } = true;
}

public sealed record ExperimentalSettings
{
    public bool UseWindowsGraphicsCapture { get; init; } = true;
    public bool EnableDataChannelControl { get; init; } = true;
}

public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int SchemaVersion { get; init; } = CurrentVersion;
    public GeneralSettings General { get; init; } = new();
    public VideoSettings Video { get; init; } = new();
    public NetworkSettings Network { get; init; } = new();
    public ConnectionSettings Connection { get; init; } = new();
    public SecuritySettings Security { get; init; } = new();
    public PrivacySettings Privacy { get; init; } = new();
    public AppearanceSettings Appearance { get; init; } = new();
    public PerformanceSettings Performance { get; init; } = new();
    public DiagnosticsSettings Diagnostics { get; init; } = new();
    public ExperimentalSettings Experimental { get; init; } = new();

    /// <summary>保留来自更高版本配置文件的未知字段，避免降级后静默丢失用户设置。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? UnknownFields { get; init; }
}

public static class QualityPresets
{
    public static VideoSettings Apply(QualityPreset preset, VideoSettings current) => preset switch
    {
        QualityPreset.Auto => current with
        {
            Preset = preset, MaxWidth = 1920, MaxHeight = 1080, TargetFps = 30,
            TargetBitrateKbps = 4000, MinBitrateKbps = 1200, MaxBitrateKbps = 8000,
            AdaptiveBitrate = true, AdaptiveFrameRate = true
        },
        QualityPreset.Smooth => current with
        {
            Preset = preset, MaxWidth = 1280, MaxHeight = 720, TargetFps = 24,
            TargetBitrateKbps = 1800, MinBitrateKbps = 600, MaxBitrateKbps = 3000
        },
        QualityPreset.Balanced => current with
        {
            Preset = preset, MaxWidth = 1920, MaxHeight = 1080, TargetFps = 30,
            TargetBitrateKbps = 4000, MinBitrateKbps = 1200, MaxBitrateKbps = 8000
        },
        QualityPreset.High => current with
        {
            Preset = preset, MaxWidth = 1920, MaxHeight = 1080, TargetFps = 60,
            TargetBitrateKbps = 8000, MinBitrateKbps = 2500, MaxBitrateKbps = 14000
        },
        QualityPreset.Ultra => current with
        {
            Preset = preset, MaxWidth = 2560, MaxHeight = 1440, TargetFps = 60,
            TargetBitrateKbps = 16000, MinBitrateKbps = 5000, MaxBitrateKbps = 28000
        },
        _ => current with { Preset = QualityPreset.Custom }
    };
}
