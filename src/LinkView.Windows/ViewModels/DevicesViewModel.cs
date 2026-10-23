using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public sealed record DeviceAddressTag(string Text, string Kind);

public partial class DevicesViewModel : ObservableObject
{
    private readonly DiscoveryService _discovery;
    private readonly DeviceStore _store;
    private readonly SettingsService _settings;
    private readonly Action<string, ToastKind> _notify;
    private readonly Action<string> _navigate;

    public DevicesViewModel(
        DiscoveryService discovery,
        DeviceStore store,
        SettingsService settings,
        Action<string> navigate,
        Action<string, ToastKind> notify)
    {
        _discovery = discovery;
        _store = store;
        _settings = settings;
        _navigate = navigate;
        _notify = notify;
        discovery.DeviceDiscovered += (_, _) => OnUi(Refresh);
        discovery.DeviceLost += (_, _) => OnUi(Refresh);
        discovery.StatusChanged += (_, status) => OnUi(() => DiscoveryStatus = status);
        Refresh();
    }

    [ObservableProperty] private string discoveryStatus = "";
    [ObservableProperty] private bool isDiscovering;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool hasAnyDevice;
    [ObservableProperty] private bool hasNearbyDevices;

    public ObservableCollection<DiscoveredDevice> Nearby { get; } = [];
    public ObservableCollection<SavedDevice> Saved { get; } = [];

    public void Refresh()
    {
        DiscoveryStatus = _discovery.IsRunning
            ? $"正在搜索局域网设备 · 已发现 {_discovery.Snapshot().Count} 台"
            : "局域网发现已关闭";
        IsDiscovering = _discovery.IsRunning;

        Nearby.Clear();
        foreach (var device in _discovery.Snapshot()
                     .Where(d => string.IsNullOrWhiteSpace(SearchText) ||
                                 d.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
        {
            Nearby.Add(device);
        }

        Saved.Clear();
        foreach (var device in _store.All.Where(d => string.IsNullOrWhiteSpace(SearchText) ||
                                                     d.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                                     (d.Note?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            Saved.Add(device);
        }

        HasAnyDevice = Nearby.Count > 0 || Saved.Count > 0;
        HasNearbyDevices = Nearby.Count > 0;
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    [RelayCommand]
    private async Task RestartDiscoveryAsync()
    {
        if (_discovery.IsRunning)
        {
            await _discovery.StopAsync();
        }

        if (!_settings.Current.Network.LanDiscoveryEnabled)
        {
            _notify("局域网发现在设置中已关闭。", ToastKind.Warning);
            return;
        }

        _discovery.Start();
        Refresh();
    }

    [RelayCommand]
    private void PairNearby(DiscoveredDevice? device)
    {
        if (device is null) return;
        _navigate("connect");
        _notify($"请创建配对码，然后让 {device.Name} 输入。", ToastKind.Info);
    }

    [RelayCommand]
    private void ToggleFavorite(SavedDevice? device)
    {
        if (device is null) return;
        _store.ToggleFavorite(device.DeviceId);
        Refresh();
    }

    [RelayCommand]
    private void Remove(SavedDevice? device)
    {
        if (device is null) return;
        _store.Remove(device.DeviceId);
        Refresh();
    }

    [RelayCommand]
    private void PairSaved(SavedDevice? device)
    {
        if (device is null) return;
        _navigate("connect");
        _notify($"与 {device.Name} 连接：请创建配对码并让对方输入。", ToastKind.Info);
    }

    [RelayCommand]
    private void CopyAddress(DiscoveredDevice? device)
    {
        if (device is null) return;
        try
        {
            Clipboard.SetText(device.Addresses.FirstOrDefault() ?? device.SourceAddress);
            _notify("地址已复制", ToastKind.Info);
        }
        catch (Exception)
        {
            _notify("无法访问剪贴板", ToastKind.Warning);
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
