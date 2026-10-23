using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LinkView.Core.Networking;

namespace LinkView.Windows.Services;

public sealed record NetworkAddressInfo(
    string InterfaceName,
    string InterfaceId,
    NetworkInterfaceType InterfaceType,
    OperationalStatus Status,
    string Address,
    IpFamily Family,
    IpScope Scope,
    bool IsUsableForConnection,
    bool SupportsMulticast,
    long SpeedBitsPerSecond);

public sealed class NetworkInfoService
{
    public IReadOnlyList<NetworkAddressInfo> GetAddresses()
    {
        var result = new List<NetworkAddressInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var supportsMulticast = nic.SupportsMulticast;
            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                {
                    continue;
                }

                // IPv4-mapped 地址是重复表示，过滤掉。
                if (address.IsIPv4MappedToIPv6)
                {
                    continue;
                }

                var scope = IpAddressTools.GetScope(address);
                if (scope == IpScope.Loopback)
                {
                    continue;
                }

                result.Add(new NetworkAddressInfo(
                    nic.Name,
                    nic.Id,
                    nic.NetworkInterfaceType,
                    nic.OperationalStatus,
                    address.ToString(),
                    IpAddressTools.GetFamily(address),
                    scope,
                    nic.OperationalStatus == OperationalStatus.Up && IpAddressTools.IsUsableForConnection(address),
                    supportsMulticast,
                    GetSpeed(nic)));
            }
        }

        return result
            .OrderBy(a => a.Family)
            .ThenByDescending(a => a.IsUsableForConnection)
            .ThenBy(a => a.InterfaceName)
            .ToArray();
    }

    public IReadOnlyList<NetworkInterface> GetMulticastInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.SupportsMulticast &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

    private static long GetSpeed(NetworkInterface nic)
    {
        try
        {
            return nic.Speed;
        }
        catch (PlatformNotSupportedException)
        {
            return 0;
        }
    }
}
