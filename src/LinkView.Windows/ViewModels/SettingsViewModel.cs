using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Core.Settings;
using LinkView.Windows.Services;
using LinkView.Windows.Services.Logging;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.ViewModels;

public sealed record SettingsSection(string Key, string Name, string Description);

public sealed record DefaultPageOption(string Key, string Label);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly SecretStore _secrets;
    private readonly DeviceStore _devices;
    private readonly RuntimeLogLevel _logLevel;
    private readonly IFileDialogService _dialogs;
    private readonly Action<string, ToastKind> _notify;

    public SettingsViewModel(
        SettingsService settings,
        ThemeService theme,
        SecretStore secrets,
        DeviceStore devices,
        RuntimeLogLevel logLevel,
        IFileDialogService dialogs,
        Action<string, ToastKind> notify)
    {
        _settings = settings;
        _theme = theme;
        _secrets = secrets;
        _devices = devices;
        _logLevel = logLevel;
        _dialogs = dialogs;
        _notify = notify;
        Draft = new SettingsDraft();
        Draft.Load(settings.Current);
        SelectedSectionKey = Sections[0].Key;
    }

    public SettingsDraft Draft { get; }

    public IReadOnlyList<SettingsSection> Sections { get; } =
    [
        new("general", "基础", "设备名称、启动行为与通知"),
        new("video", "画质", "分辨率、帧率、码率与编码"),
        new("network", "网络", "IPv4/IPv6、STUN、TURN 与超时"),
        new("connection", "连接与安全", "配对确认、会话与凭据"),
        new("privacy", "隐私", "本地记录与日志脱敏"),
        new("appearance", "界面", "主题、强调色、动效与字号"),
        new("performance", "性能", "硬件加速与统计频率"),
        new("diagnostics", "诊断", "日志等级与保留期限"),
        new("experimental", "实验性", "未经过完整验证的功能开关")
    ];

    public IReadOnlyList<DefaultPageOption> DefaultPages { get; } =
    [
        new("home", "首页"),
        new("devices", "设备中心"),
        new("connect", "连接中心"),
        new("share", "屏幕共享"),
        new("monitor", "性能监控")
    ];

    public IReadOnlyList<ThemeMode> ThemeOptions { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<NetworkPreference> NetworkOptions { get; } =
    [
        NetworkPreference.Auto, NetworkPreference.Ipv4Preferred, NetworkPreference.Ipv6Preferred,
        NetworkPreference.Ipv4Only, NetworkPreference.Ipv6Only
    ];
    public IReadOnlyList<QualityPreset> PresetOptions { get; } = Enum.GetValues<QualityPreset>();
    public IReadOnlyList<VideoCodecPreference> CodecOptions { get; } = [VideoCodecPreference.Auto, VideoCodecPreference.Vp8];
    public IReadOnlyList<ScaleMode> ScaleOptions { get; } = Enum.GetValues<ScaleMode>();
    public IReadOnlyList<AnimationLevel> AnimationOptions { get; } = Enum.GetValues<AnimationLevel>();
    public IReadOnlyList<InterfaceDensity> DensityOptions { get; } = Enum.GetValues<InterfaceDensity>();
    public IReadOnlyList<IceTransportMode> IceOptions { get; } = Enum.GetValues<IceTransportMode>();
    public IReadOnlyList<AppLogLevel> LogLevelOptions { get; } = Enum.GetValues<AppLogLevel>();
    public IReadOnlyList<int> FpsOptions { get; } = [15, 24, 30, 60];

    public IReadOnlyList<string> AccentOptions { get; } =
        ["#2F6FEB", "#0E8F7E", "#7C5CE6", "#C05621", "#C23B6B", "#3A7D44", "#0E7490"];

    [ObservableProperty] private string selectedSectionKey = "general";
    [ObservableProperty] private string statusText = "修改后点击保存。";
    [ObservableProperty] private bool hasErrors;
    [ObservableProperty] private string? credentialHint;

    public ObservableCollection<SettingsIssue> Issues { get; } = [];

    partial void OnSelectedSectionKeyChanged(string value) => UpdateCredentialHint();

    public void ReloadFromService()
    {
        Draft.Load(_settings.Current);
        Issues.Clear();
        HasErrors = false;
        StatusText = "已重新载入当前配置。";
        UpdateCredentialHint();
    }

    [RelayCommand]
    private void Save()
    {
        Issues.Clear();
        var candidate = Draft.ToSettings(_settings.Current);
        var issues = _settings.Validate(candidate);
        foreach (var issue in issues.Where(i => i.Severity != SettingsIssueSeverity.Info))
        {
            Issues.Add(issue);
        }

        HasErrors = issues.Any(i => i.Severity == SettingsIssueSeverity.Error);
        if (HasErrors)
        {
            StatusText = "存在不合法设置，未保存。";
            _notify("设置未保存：请先修正错误项。", ToastKind.Error);
            return;
        }

        if (!_settings.Save(candidate, out _))
        {
            StatusText = "保存失败。";
            return;
        }

        ApplyTurnSecret();
        ApplyAppearance();
        StatusText = "已保存。界面与日志设置即时生效，网络与画质设置在下一次连接时生效。";
        _notify("设置已保存", ToastKind.Success);
        UpdateCredentialHint();
    }

    private void ApplyTurnSecret()
    {
        if (string.IsNullOrWhiteSpace(Draft.TurnUri))
        {
            return;
        }

        var key = SessionCoordinator.SecretKeyForTurn(Draft.TurnUri.Trim());
        if (Draft.ClearTurnCredential)
        {
            _secrets.Set(key, null);
        }
        else if (!string.IsNullOrEmpty(Draft.TurnCredential))
        {
            _secrets.Set(key, Draft.TurnCredential);
        }

        Draft.TurnCredential = "";
        Draft.ClearTurnCredential = false;
    }

    private void ApplyAppearance()
    {
        var appearance = _settings.Current.Appearance;
        _theme.Apply(appearance);
        _logLevel.Level = Map(_settings.Current.Diagnostics.LogLevel);
        _logLevel.RetentionDays = _settings.Current.Diagnostics.LogRetentionDays;
        _devices.Prune(_settings.Current.Privacy.RecentDeviceLimit);
    }

    private void UpdateCredentialHint()
    {
        var uri = Draft.TurnUri?.Trim();
        if (string.IsNullOrWhiteSpace(uri))
        {
            CredentialHint = null;
            return;
        }

        CredentialHint = _secrets.Get(SessionCoordinator.SecretKeyForTurn(uri)) is null
            ? "尚未保存该 TURN 服务器的凭据。"
            : "已保存凭据（不会显示明文）。留空表示保持不变。";
    }

    [RelayCommand]
    private void SetAccent(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;
        Draft.AccentColor = color;
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!_dialogs.Confirm("恢复默认设置", "所有自定义设置将恢复为默认值，包括网络与画质配置。是否继续？"))
        {
            return;
        }

        _settings.ResetToDefaults();
        Draft.Load(_settings.Current);
        ApplyAppearance();
        Issues.Clear();
        HasErrors = false;
        StatusText = "已恢复默认设置。";
        _notify("已恢复默认设置", ToastKind.Info);
    }

    [RelayCommand]
    private void Import()
    {
        var path = _dialogs.PickOpenFile("LinkView 配置 (*.json)|*.json|所有文件 (*.*)|*.*", "导入配置");
        if (path is null) return;
        var (ok, error) = _settings.Import(path);
        if (!ok)
        {
            _notify("导入失败：" + error, ToastKind.Error);
            StatusText = "导入失败：" + error;
            return;
        }

        Draft.Load(_settings.Current);
        ApplyAppearance();
        Issues.Clear();
        HasErrors = false;
        StatusText = "配置已导入并保存。";
        _notify("配置已导入", ToastKind.Success);
    }

    [RelayCommand]
    private void Export()
    {
        var path = _dialogs.PickSaveFile("LinkView 配置 (*.json)|*.json", "导出配置",
            $"linkview-settings-{DateTime.Now:yyyyMMdd}.json");
        if (path is null) return;
        _settings.Export(path);
        _notify("配置已导出到 " + path, ToastKind.Success);
    }

    private static LogLevel Map(AppLogLevel level) => level switch
    {
        AppLogLevel.Trace => LogLevel.Trace,
        AppLogLevel.Debug => LogLevel.Debug,
        AppLogLevel.Warning => LogLevel.Warning,
        AppLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Information
    };
}
