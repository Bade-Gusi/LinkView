using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Core.Sessions;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SessionCoordinator _session;
    private readonly SettingsService _settings;
    private readonly DeviceStore _devices;
    private readonly DiscoveryService _discovery;
    private readonly NetworkInfoService _network;
    private readonly IdentityService _identity;
    private readonly Action<string> _navigate;
    private readonly Action<string, ToastKind> _notify;
    private readonly ConnectViewModel _connect;

    public HomeViewModel(
        SessionCoordinator session,
        SettingsService settings,
        DeviceStore devices,
        DiscoveryService discovery,
        NetworkInfoService network,
        IdentityService identity,
        ConnectViewModel connect,
        Action<string> navigate,
        Action<string, ToastKind> notify)
    {
        _session = session;
        _settings = settings;
        _devices = devices;
        _discovery = discovery;
        _network = network;
        _identity = identity;
        _connect = connect;
        _navigate = navigate;
        _notify = notify;

        discovery.DeviceDiscovered += (_, _) => OnUi(RefreshDiscovery);
        discovery.DeviceLost += (_, _) => OnUi(RefreshDiscovery);
        discovery.StatusChanged += (_, status) => OnUi(() => DiscoveryStatus = status);
        session.StateChanged += (_, _) => OnUi(RefreshSession);
        Refresh();
    }

    [ObservableProperty] private string deviceName = "";
    [ObservableProperty] private string deviceIdShort = "";
    [ObservableProperty] private string ipv4Summary = "";
    [ObservableProperty] private string ipv6Summary = "";
    [ObservableProperty] private string networkHint = "";
    [ObservableProperty] private string discoveryStatus = "尚未开始搜索";
    [ObservableProperty] private int discoveredCount;
    [ObservableProperty] private string sessionStateLabel = "准备就绪";
    [ObservableProperty] private string sessionDescription = "";
    [ObservableProperty] private string peerName = "尚未连接";
    [ObservableProperty] private bool hasSession;
    [ObservableProperty] private bool isSharing;
    [ObservableProperty] private string joinCode = "";
    [ObservableProperty] private string? healthNote;
    [ObservableProperty] private bool hasRecentDevices;
    [ObservableProperty] private bool hasNearbyDevices;

    public ObservableCollection<DiscoveredDevice> NearbyDevices { get; } = [];
    public ObservableCollection<SavedDevice> RecentDevices { get; } = [];

    public string AppVersion => typeof(HomeViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";

    public void Refresh()
    {
        var settings = _settings.Current;
        DeviceName = settings.General.DeviceName;
        DeviceIdShort = Formatters.ShortDeviceId(_identity.Identity.DeviceId);
        var addresses = _network.GetAddresses();
        Ipv4Summary = Formatters.Addresses(addresses.Where(a => a.IsUsableForConnection && a.Family == Core.Networking.IpFamily.IPv4).Select(a => a.Address));
        Ipv6Summary = Formatters.Addresses(addresses.Where(a => a.IsUsableForConnection && a.Family == Core.Networking.IpFamily.IPv6).Select(a => a.Address));
        NetworkHint = $"{addresses.Select(a => a.InterfaceId).Distinct().Count()} 个网络接口";
        DiscoveryStatus = _discovery.IsRunning ? "正在局域网上搜索设备" : "局域网发现已关闭";
        HealthNote = settings.Network.SignalingServer.StartsWith("ws://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            ? "信令服务默认指向本机。若需要跨设备连接，请在设置中配置可访问的信令服务。"
            : null;
        RefreshDiscovery();
        RefreshSession();
        RefreshRecent();
    }

    private void RefreshDiscovery()
    {
        var snapshot = _discovery.Snapshot();
        DiscoveredCount = snapshot.Count;
        HasNearbyDevices = snapshot.Count > 0;
        NearbyDevices.Clear();
        foreach (var device in snapshot.Take(4))
        {
            NearbyDevices.Add(device);
        }
    }

    private void RefreshRecent()
    {
        RecentDevices.Clear();
        foreach (var device in _devices.All.Take(4))
        {
            RecentDevices.Add(device);
        }

        HasRecentDevices = RecentDevices.Count > 0;
    }

    private void RefreshSession()
    {
        SessionStateLabel = Formatters.StateDescription(_session.State) is { Length: > 0 } d && _session.State != ShareSessionState.Idle
            ? Converters.SessionLabels.Describe(_session.State)
            : "准备就绪";
        SessionDescription = Formatters.StateDescription(_session.State);
        PeerName = _session.PeerDevice?.DeviceName ?? "尚未连接";
        HasSession = _session.State is ShareSessionState.Connected or ShareSessionState.Sharing or ShareSessionState.Reconnecting;
        IsSharing = _session.State == ShareSessionState.Sharing;
    }

    [RelayCommand]
    private void CreatePairing()
    {
        _navigate("connect");
        _connect.ModeIndex = 0;
        _ = _connect.CreatePairingCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (string.IsNullOrWhiteSpace(JoinCode))
        {
            _notify("请输入 6 位配对码。", ToastKind.Warning);
            return;
        }

        _navigate("connect");
        await _connect.JoinWithCodeAsync(JoinCode);
    }

    [RelayCommand]
    private void OpenShare() => _navigate("share");

    [RelayCommand]
    private void OpenConnect() => _navigate("connect");

    [RelayCommand]
    private void OpenDevices() => _navigate("devices");

    [RelayCommand]
    private void OpenSettings() => _navigate("settings");

    [RelayCommand]
    private void PairNearby(DiscoveredDevice? device)
    {
        if (device is null) return;
        _navigate("connect");
        _connect.ModeIndex = 0;
        _notify($"请创建配对码，并让 {device.Name} 输入该配对码。", ToastKind.Info);
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
