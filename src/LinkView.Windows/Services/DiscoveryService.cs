using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LinkView.Core.Networking;
using LinkView.Protocol;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services;

public sealed record DiscoveredDevice
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required DeviceType DeviceType { get; init; }
    public required string AppVersion { get; init; }
    public required IReadOnlyList<string> Addresses { get; init; }
    public required string SourceAddress { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
}

/// <summary>
/// 局域网设备发现。使用 IPv4 与 IPv6 多播同时广播。
/// 多播在部分 Windows 防火墙、企业 Wi-Fi 或子网隔离环境中会被丢弃，
/// 因此发现失败不会阻止通过配对码或手动地址连接。
/// </summary>
public sealed class DiscoveryService : IAsyncDisposable
{
    private const string GroupV4 = "239.255.76.86";
    private const string GroupV6 = "ff12::4c56";
    private const int Port = 45892;
    private const int AnnounceIntervalMs = 2000;
    private static readonly TimeSpan DeviceTtl = TimeSpan.FromSeconds(10);

    private readonly AppPaths _paths;
    private readonly IdentityService _identity;
    private readonly SettingsService _settings;
    private readonly NetworkInfoService _networkInfo;
    private readonly ILogger<DiscoveryService> _logger;
    private readonly Dictionary<string, DiscoveredDevice> _devices = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _v4Task;
    private Task? _v6Task;
    private Task? _expiryTask;

    public DiscoveryService(
        AppPaths paths,
        IdentityService identity,
        SettingsService settings,
        NetworkInfoService networkInfo,
        ILogger<DiscoveryService> logger)
    {
        _paths = paths;
        _identity = identity;
        _settings = settings;
        _networkInfo = networkInfo;
        _logger = logger;
    }

    public event EventHandler<DiscoveredDevice>? DeviceDiscovered;
    public event EventHandler<string>? DeviceLost;
    public event EventHandler<string>? StatusChanged;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public IReadOnlyList<DiscoveredDevice> Snapshot()
    {
        lock (_sync)
        {
            return _devices.Values.OrderByDescending(d => d.LastSeen).ToArray();
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _v4Task = Task.Run(() => RunIpv4Async(token), token);
        _v6Task = Task.Run(() => RunIpv6Async(token), token);
        _expiryTask = Task.Run(() => RunExpiryAsync(token), token);
        _logger.LogInformation("discovery_started");
        StatusChanged?.Invoke(this, "正在局域网上搜索设备");
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            await Task.WhenAll(_v4Task ?? Task.CompletedTask, _v6Task ?? Task.CompletedTask,
                _expiryTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
        _cts = null;
        lock (_sync)
        {
            _devices.Clear();
        }

        _logger.LogInformation("discovery_stopped");
        StatusChanged?.Invoke(this, "局域网发现已停止");
    }

    private async Task RunIpv4Async(CancellationToken ct)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            client.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            client.JoinMulticastGroup(IPAddress.Parse(GroupV4));
            client.MulticastLoopback = false;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "discovery_ipv4_bind_failed");
            StatusChanged?.Invoke(this, "IPv4 多播不可用，仍可通过配对码连接");
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var announceTask = AnnounceIpv4Async(client, linked.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(linked.Token);
                if (result.Buffer.Length > 4096)
                {
                    continue;
                }

                HandleAnnouncement(result.Buffer, result.RemoteEndPoint.Address.ToString());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "discovery_ipv4_receive_failed");
        }
        finally
        {
            linked.Cancel();
            try { await announceTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task AnnounceIpv4Async(UdpClient client, CancellationToken ct)
    {
        var group = new IPEndPoint(IPAddress.Parse(GroupV4), Port);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var bytes = BuildAnnouncement();
                await client.SendAsync(bytes, group, ct);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) break;
            }

            await Task.Delay(AnnounceIntervalMs, ct);
        }
    }

    private async Task RunIpv6Async(CancellationToken ct)
    {
        Socket socket;
        try
        {
            socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "discovery_ipv6_unavailable");
            return;
        }

        using (socket)
        {
            var group = IPAddress.Parse(GroupV6);
            var joined = 0;
            var interfaces = _networkInfo.GetMulticastInterfaces();
            foreach (var nic in interfaces)
            {
                try
                {
                    var index = nic.GetIPProperties().GetIPv6Properties().Index;
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership,
                        new IPv6MulticastOption(group, index));
                    joined++;
                }
                catch (Exception ex) when (ex is SocketException or NetworkInformationException or InvalidOperationException)
                {
                }
            }

            if (joined == 0)
            {
                _logger.LogInformation("discovery_ipv6_no_multicast_interface");
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var announceTask = AnnounceIpv6Async(socket, interfaces, linked.Token);
            try
            {
                var buffer = new byte[4096];
                EndPoint remote = new IPEndPoint(IPAddress.IPv6Any, 0);
                while (!linked.IsCancellationRequested)
                {
                    var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, linked.Token);
                    if (result.ReceivedBytes is <= 0 or > 4096)
                    {
                        continue;
                    }

                    if (result.RemoteEndPoint is IPEndPoint ep)
                    {
                        HandleAnnouncement(buffer.AsSpan(0, result.ReceivedBytes), ep.Address.ToString());
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("discovery_ipv6_receive_failed {Error}", ex.Message);
            }
            finally
            {
                linked.Cancel();
                try { await announceTask; } catch (OperationCanceledException) { }
            }
        }
    }

    private async Task AnnounceIpv6Async(Socket socket, IReadOnlyList<NetworkInterface> interfaces, CancellationToken ct)
    {
        var group = IPAddress.Parse(GroupV6);
        while (!ct.IsCancellationRequested)
        {
            var bytes = BuildAnnouncement();
            foreach (var nic in interfaces)
            {
                try
                {
                    var index = nic.GetIPProperties().GetIPv6Properties().Index;
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, index);
                    var scopedGroup = new IPAddress(group.GetAddressBytes(), index);
                    var endpoint = new IPEndPoint(scopedGroup, Port);
                    await socket.SendToAsync(bytes, SocketFlags.None, endpoint, ct);
                }
                catch (Exception ex) when (ex is SocketException or NetworkInformationException or InvalidOperationException)
                {
                }
            }

            await Task.Delay(AnnounceIntervalMs, ct);
        }
    }

    private async Task RunExpiryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            List<string> expired = [];
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var pair in _devices.Where(p => now - p.Value.LastSeen > DeviceTtl).ToArray())
                {
                    _devices.Remove(pair.Key);
                    expired.Add(pair.Key);
                }
            }

            foreach (var id in expired)
            {
                DeviceLost?.Invoke(this, id);
            }
        }
    }

    private byte[] BuildAnnouncement()
    {
        var usableAddresses = _networkInfo.GetAddresses()
            .Where(a => a.IsUsableForConnection)
            .Select(a => a.Address)
            .Distinct()
            .Take(8)
            .ToArray();

        var payload = new
        {
            v = ProtocolConstants.Version,
            deviceId = _identity.Identity.DeviceId,
            name = _settings.Current.General.DeviceName,
            type = "windows",
            appVersion = typeof(DiscoveryService).Assembly.GetName().Version?.ToString() ?? "0.9.0",
            port = Port,
            addresses = usableAddresses,
            capabilities = new[] { "vp8", "recv", "send" }
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private void HandleAnnouncement(ReadOnlySpan<byte> data, string sourceAddress)
    {
        try
        {
            using var doc = JsonDocument.Parse(data.ToArray());
            var root = doc.RootElement;
            if (root.GetProperty("v").GetInt32() != ProtocolConstants.Version)
            {
                return;
            }

            var deviceId = root.GetProperty("deviceId").GetString();
            var name = root.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(name) ||
                deviceId == _identity.Identity.DeviceId)
            {
                return;
            }

            var typeText = root.GetProperty("type").GetString();
            var deviceType = typeText == "android" ? DeviceType.Android : DeviceType.Windows;
            var appVersion = root.TryGetProperty("appVersion", out var av) ? av.GetString() ?? "" : "";
            var addresses = new List<string>();
            if (root.TryGetProperty("addresses", out var addrArray) && addrArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in addrArray.EnumerateArray())
                {
                    var text = item.GetString();
                    if (text is null || !IPAddress.TryParse(text, out var parsed))
                    {
                        continue;
                    }

                    if (IpAddressTools.IsUsableForConnection(parsed))
                    {
                        addresses.Add(parsed.ToString());
                    }
                }
            }

            if (!addresses.Contains(sourceAddress) && IPAddress.TryParse(sourceAddress, out var src) &&
                IpAddressTools.IsUsableForConnection(src))
            {
                addresses.Add(sourceAddress);
            }

            var capabilities = root.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
                ? caps.EnumerateArray().Select(c => c.GetString()).Where(c => c is not null).Cast<string>().ToArray()
                : [];

            var device = new DiscoveredDevice
            {
                DeviceId = deviceId,
                Name = name,
                DeviceType = deviceType,
                AppVersion = appVersion,
                Addresses = addresses,
                SourceAddress = sourceAddress,
                LastSeen = DateTimeOffset.UtcNow,
                Capabilities = capabilities
            };

            bool isNew;
            lock (_sync)
            {
                isNew = !_devices.ContainsKey(deviceId);
                _devices[deviceId] = device;
            }

            DeviceDiscovered?.Invoke(this, device);
        }
        catch (JsonException)
        {
        }
        catch (KeyNotFoundException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
