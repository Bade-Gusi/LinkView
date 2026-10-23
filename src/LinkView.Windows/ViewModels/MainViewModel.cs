using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Core.Errors;
using LinkView.Core.Sessions;
using LinkView.Protocol;
using LinkView.Windows.Media;
using LinkView.Windows.Services;
using LinkView.Windows.Services.Logging;

namespace LinkView.Windows.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SessionCoordinator _session;
    private readonly SettingsService _settings;
    private readonly DiscoveryService _discovery;
    private readonly DeviceStore _devices;
    private readonly IdentityService _identity;
    private readonly NetworkInfoService _network;
    private readonly DispatcherTimer _toastTimer;
    private bool _disposed;

    public MainViewModel(
        SessionCoordinator session,
        SettingsService settings,
        ThemeService theme,
        DiscoveryService discovery,
        DeviceStore devices,
        IdentityService identity,
        NetworkInfoService network,
        ScreenCaptureService capture,
        ConnectionHistoryService history,
        SecretStore secrets,
        DiagnosticsService diagnostics,
        LogStore logs,
        IFileDialogService dialogs,
        RuntimeLogLevel logLevel)
    {
        _session = session;
        _settings = settings;
        _discovery = discovery;
        _devices = devices;
        _identity = identity;
        _network = network;

        Theme = theme;
        Theme.Initialize(settings.Current.Appearance);

        Connect = new ConnectViewModel(session, settings, ShowToast);
        Home = new HomeViewModel(session, settings, devices, discovery, network, identity, Connect, Navigate, ShowToast);
        Devices = new DevicesViewModel(discovery, devices, settings, Navigate, ShowToast);
        Share = new ShareViewModel(session, capture, settings, Navigate,
            vm => ViewerRequested?.Invoke(this, vm), ShowToast);
        Monitor = new MonitorViewModel(session, settings);
        Diagnostics = new DiagnosticsViewModel(diagnostics, network, ShowToast);
        History = new HistoryViewModel(history, settings, ShowToast);
        Settings = new SettingsViewModel(settings, theme, secrets, devices, logLevel, dialogs, ShowToast);
        Logs = new LogsViewModel(logs, diagnostics, ShowToast);
        About = new AboutViewModel(identity);

        NavItems =
        [
            new("home", "首页", "当前设备与连接概览", "\uE80F", Home),
            new("devices", "设备中心", "发现、收藏与管理设备", "\uE772", Devices),
            new("connect", "连接中心", "通过一次性配对码建立会话", "\uE71B", Connect),
            new("share", "屏幕共享", "选择采集源并控制实时共享", "\uE7F4", Share),
            new("monitor", "性能监控", "实时传输与媒体指标", "\uE9D2", Monitor),
            new("diagnostics", "网络诊断", "真实网络与连接检查", "\uE9D9", Diagnostics),
            new("history", "连接历史", "本地连接记录", "\uE81C", History),
            new("settings", "高级设置", "画质、网络、安全与界面", "\uE713", Settings),
            new("logs", "日志与诊断", "结构化日志与诊断导出", "\uE9D5", Logs),
            new("about", "关于 LinkView", "版本、依赖与隐私说明", "\uE946", About)
        ];

        var defaultKey = settings.Current.General.DefaultPage;
        SelectedNav = NavItems.FirstOrDefault(n => n.Key == defaultKey) ?? NavItems[0];

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _toastTimer.Tick += (_, _) => HideToast();

        session.StateChanged += OnSessionStateChanged;
        session.ErrorOccurred += OnSessionError;
        session.IncomingJoinRequest += OnIncomingJoinRequest;
        session.PeerJoined += OnPeerJoined;
        session.PeerLeft += OnPeerLeft;
        session.Notice += OnNotice;
        discovery.DeviceDiscovered += (_, _) => OnUi(UpdateDiscoverySummary);
        discovery.DeviceLost += (_, _) => OnUi(UpdateDiscoverySummary);
        settings.Changed += (_, _) => OnUi(UpdateDeviceInfo);

        UpdateDeviceInfo();
        UpdateDiscoverySummary();
        RefreshSessionUi();

        if (settings.Current.Network.LanDiscoveryEnabled)
        {
            discovery.Start();
        }
    }

    public event EventHandler<ShareViewModel>? ViewerRequested;

    public HomeViewModel Home { get; }
    public DevicesViewModel Devices { get; }
    public ConnectViewModel Connect { get; }
    public ShareViewModel Share { get; }
    public MonitorViewModel Monitor { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }
    public AboutViewModel About { get; }
    public ThemeService Theme { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem? selectedNav;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private string currentTitle = "首页";
    [ObservableProperty] private string currentDescription = "当前设备与连接概览";
    [ObservableProperty] private ShareSessionState sessionState = ShareSessionState.Idle;
    [ObservableProperty] private string sessionStateLabel = "准备就绪";
    [ObservableProperty] private string stateDescription = "选择一项操作开始";
    [ObservableProperty] private string peerName = "尚未连接";
    [ObservableProperty] private bool isSessionActive;
    [ObservableProperty] private bool isSharing;
    [ObservableProperty] private string deviceName = "";
    [ObservableProperty] private string deviceIdShort = "";
    [ObservableProperty] private string networkSummary = "";
    [ObservableProperty] private int discoveredCount;
    [ObservableProperty] private string discoverySummary = "局域网发现已停止";
    [ObservableProperty] private DeviceSummary? pendingJoinDevice;
    [ObservableProperty] private string toastText = "";
    [ObservableProperty] private ToastKind toastKind;
    [ObservableProperty] private bool isToastVisible;
    [ObservableProperty] private string? lastErrorMessage;

    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";
    public bool HasPendingJoin => PendingJoinDevice is not null;
    public string PendingJoinName => PendingJoinDevice?.DeviceName ?? "";
    public string PendingJoinDetail => PendingJoinDevice is null ? "" :
        $"{(PendingJoinDevice.DeviceType == DeviceType.Android ? "Android 手机" : "Windows 电脑")} · {PendingJoinDevice.ObservedAddress ?? "地址未知"}";

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        CurrentPage = value.Page;
        CurrentTitle = value.Label;
        CurrentDescription = value.Description;
        ActivatePage(value.Key);
    }

    partial void OnPendingJoinDeviceChanged(DeviceSummary? value)
    {
        OnPropertyChanged(nameof(HasPendingJoin));
        OnPropertyChanged(nameof(PendingJoinName));
        OnPropertyChanged(nameof(PendingJoinDetail));
    }

    private void ActivatePage(string key)
    {
        Monitor.Deactivate();
        switch (key)
        {
            case "home": Home.Refresh(); break;
            case "devices": Devices.Refresh(); break;
            case "share": Share.ReloadSources(); Share.RefreshState(); break;
            case "monitor": Monitor.Activate(); break;
            case "diagnostics": Diagnostics.ReloadInterfaces(); break;
            case "history": History.Refresh(); break;
            case "settings": Settings.ReloadFromService(); break;
            case "logs": Logs.Reload(); break;
            case "connect": Connect.Reset(); break;
        }
    }

    public void Navigate(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            SelectedNav = item;
        }
    }

    public void ShowToast(string message, ToastKind kind)
    {
        OnUi(() =>
        {
            ToastText = message;
            ToastKind = kind;
            IsToastVisible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        });
    }

    [RelayCommand]
    private void DismissToast() => HideToast();

    private void HideToast()
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    [RelayCommand]
    private async Task AcceptJoinAsync()
    {
        var device = PendingJoinDevice;
        PendingJoinDevice = null;
        if (device is not null)
        {
            _devices.Upsert(device);
        }

        await _session.AcceptJoinAsync();
        ShowToast("已允许连接，正在建立会话…", ToastKind.Success);
    }

    [RelayCommand]
    private async Task RejectJoinAsync()
    {
        PendingJoinDevice = null;
        await _session.RejectJoinAsync();
        ShowToast("已拒绝连接请求", ToastKind.Info);
    }

    [RelayCommand]
    private async Task StopSharingAsync()
    {
        await _session.StopSharingAsync();
    }

    [RelayCommand]
    private void OpenSettings() => Navigate("settings");

    [RelayCommand]
    private void OpenDiagnostics() => Navigate("diagnostics");

    private void OnSessionStateChanged(object? sender, SessionStateChange change) => OnUi(() =>
    {
        SessionState = change.To;
        SessionStateLabel = Converters.SessionLabels.Describe(change.To);
        StateDescription = Formatters.StateDescription(change.To);
        PeerName = _session.PeerDevice?.DeviceName ?? "尚未连接";
        IsSessionActive = change.To is ShareSessionState.Pairing or ShareSessionState.AwaitingConsent or
            ShareSessionState.Negotiating or ShareSessionState.Connecting or ShareSessionState.Connected or
            ShareSessionState.Sharing or ShareSessionState.Reconnecting;
        IsSharing = change.To == ShareSessionState.Sharing;
        Share.RefreshState();
        if (change.To is ShareSessionState.Disconnected or ShareSessionState.Failed or ShareSessionState.Idle)
        {
            PendingJoinDevice = null;
        }
    });

    private void RefreshSessionUi()
    {
        SessionState = _session.State;
        SessionStateLabel = Converters.SessionLabels.Describe(_session.State);
        StateDescription = Formatters.StateDescription(_session.State);
        PeerName = _session.PeerDevice?.DeviceName ?? "尚未连接";
        IsSessionActive = _session.State is not (ShareSessionState.Idle or ShareSessionState.Disconnected);
        IsSharing = _session.State == ShareSessionState.Sharing;
    }

    private void OnSessionError(object? sender, LinkViewError error) => OnUi(() =>
    {
        LastErrorMessage = error.UserMessage;
        ShowToast(error.UserMessage, ToastKind.Error);
    });

    private void OnIncomingJoinRequest(object? sender, DeviceSummary device) => OnUi(() =>
    {
        PendingJoinDevice = device;
        ShowToast($"{device.DeviceName} 请求连接", ToastKind.Info);
    });

    private void OnPeerJoined(object? sender, (DeviceSummary Peer, SessionRole Role) e) => OnUi(() =>
    {
        PeerName = e.Peer.DeviceName;
        ShowToast($"已与 {e.Peer.DeviceName} 建立会话", ToastKind.Success);
        Home.Refresh();
    });

    private void OnPeerLeft(object? sender, string reason) => OnUi(() =>
    {
        PendingJoinDevice = null;
        ShowToast(reason, ToastKind.Warning);
        Home.Refresh();
    });

    private void OnNotice(object? sender, string message) => OnUi(() => ShowToast(message, ToastKind.Info));

    private void UpdateDeviceInfo()
    {
        DeviceName = _settings.Current.General.DeviceName;
        DeviceIdShort = Formatters.ShortDeviceId(_identity.Identity.DeviceId);
        var addresses = _network.GetAddresses();
        var v4 = addresses.Count(a => a.IsUsableForConnection && a.Family == Core.Networking.IpFamily.IPv4);
        var v6 = addresses.Count(a => a.IsUsableForConnection && a.Family == Core.Networking.IpFamily.IPv6);
        NetworkSummary = $"IPv4 {v4} · IPv6 {v6}";
    }

    private void UpdateDiscoverySummary()
    {
        DiscoveredCount = _discovery.Snapshot().Count;
        DiscoverySummary = _discovery.IsRunning
            ? $"局域网发现运行中 · {DiscoveredCount} 台设备"
            : "局域网发现已停止";
    }

    public void RefreshForSettingsChange()
    {
        UpdateDeviceInfo();
        Home.Refresh();
        Share.RefreshState();
        Connect.Reset();
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _toastTimer.Stop();
        _session.StateChanged -= OnSessionStateChanged;
        _session.ErrorOccurred -= OnSessionError;
        _session.IncomingJoinRequest -= OnIncomingJoinRequest;
        _session.PeerJoined -= OnPeerJoined;
        _session.PeerLeft -= OnPeerLeft;
        _session.Notice -= OnNotice;
        Monitor.Deactivate();
    }
}
