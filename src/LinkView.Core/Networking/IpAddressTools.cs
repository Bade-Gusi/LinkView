using System.Net;
using System.Net.Sockets;

namespace LinkView.Core.Networking;

public enum IpScope
{
    Loopback,
    LinkLocal,
    Private,
    GlobalUnicast,
    Multicast,
    Unspecified,
    Unsupported
}

public enum IpFamily
{
    IPv4,
    IPv6
}

/// <summary>
/// IPv4/IPv6 地址处理的唯一入口。界面、日志和诊断都必须通过该类判断地址作用域，
/// 避免把 IPv6 链路本地地址误当成公网可达地址。
/// </summary>
public static class IpAddressTools
{
    public static IpFamily GetFamily(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? IpFamily.IPv6 : IpFamily.IPv4;

    public static IpScope GetScope(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b = stackalloc byte[4];
            address.TryWriteBytes(b, out _);
            if (b[0] == 127) return IpScope.Loopback;
            if (b[0] == 169 && b[1] == 254) return IpScope.LinkLocal;
            if (b[0] == 10) return IpScope.Private;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return IpScope.Private;
            if (b[0] == 192 && b[1] == 168) return IpScope.Private;
            if (b[0] >= 224 && b[0] <= 239) return IpScope.Multicast;
            if (b[0] == 0) return IpScope.Unspecified;
            return IpScope.GlobalUnicast;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Loopback)) return IpScope.Loopback;
            if (address.Equals(IPAddress.IPv6Any)) return IpScope.Unspecified;
            if (address.IsIPv6LinkLocal) return IpScope.LinkLocal;
            if (address.IsIPv6SiteLocal) return IpScope.Private;
            if (address.IsIPv6Multicast) return IpScope.Multicast;
            Span<byte> b = stackalloc byte[16];
            address.TryWriteBytes(b, out _);
            // fc00::/7 — 唯一本地地址（ULA）
            if ((b[0] & 0xFE) == 0xFC) return IpScope.Private;
            // IPv4-mapped (::ffff:0:0/96) 按嵌入的 IPv4 处理。
            if (address.IsIPv4MappedToIPv6) return GetScope(address.MapToIPv4());
            return IpScope.GlobalUnicast;
        }

        return IpScope.Unsupported;
    }

    /// <summary>
    /// 该地址是否适合作为普通业务连接的候选地址。链路本地、回环、未指定地址默认排除；
    /// 链路本地 IPv6 仅在显式允许同链路场景时使用。
    /// </summary>
    public static bool IsUsableForConnection(IPAddress address, bool allowLinkLocal = false)
    {
        var scope = GetScope(address);
        return scope switch
        {
            IpScope.GlobalUnicast or IpScope.Private => true,
            IpScope.LinkLocal => allowLinkLocal,
            _ => false
        };
    }

    /// <summary>格式化主机和端口。IPv6 必须带方括号，否则 host:port 会产生歧义。</summary>
    public static string FormatEndpoint(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var normalized = host.Trim();
        if (normalized.StartsWith('[') && normalized.EndsWith(']'))
        {
            normalized = normalized[1..^1];
        }

        if (IPAddress.TryParse(normalized, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{address}]:{port}";
        }

        return $"{normalized}:{port}";
    }

    public static string FormatEndpoint(IPEndPoint endpoint) => FormatEndpoint(endpoint.Address.ToString(), endpoint.Port);

    /// <summary>
    /// 解析用户输入的主机/端点。
    /// 支持：1.2.3.4、1.2.3.4:8080、[fe80::1%12]:8080、fe80::1%12、example.com:443。
    /// 返回 false 表示格式非法，调用方应给出针对性提示而不是笼统的“连接失败”。
    /// </summary>
    public static bool TryParseEndpoint(string input, out string host, out int port, out string? error)
    {
        host = string.Empty;
        port = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "地址不能为空";
            return false;
        }

        var text = input.Trim();

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                error = "IPv6 地址缺少右方括号";
                return false;
            }

            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.StartsWith(':'))
            {
                if (!TryParsePort(rest[1..], out port, out error)) return false;
            }
            else if (rest.Length > 0)
            {
                error = "方括号后只能跟端口";
                return false;
            }

            return ValidateHost(host, out error);
        }

        // 未加方括号但包含多个冒号：几乎一定是 IPv6，且不允许再附带端口。
        if (text.Count(c => c == ':') > 1)
        {
            if (!IPAddress.TryParse(text, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                error = "IPv6 地址格式非法";
                return false;
            }

            host = text;
            return true;
        }

        var colon = text.LastIndexOf(':');
        if (colon > 0)
        {
            var possibleHost = text[..colon];
            if (!TryParsePort(text[(colon + 1)..], out port, out error)) return false;
            host = possibleHost;
            return ValidateHost(host, out error);
        }

        host = text;
        return ValidateHost(host, out error);
    }

    private static bool TryParsePort(string value, out int port, out string? error)
    {
        error = null;
        if (!int.TryParse(value, out port) || port is < 1 or > 65535)
        {
            error = "端口必须是 1 到 65535 之间的整数";
            port = 0;
            return false;
        }

        return true;
    }

    private static bool ValidateHost(string host, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "主机地址不能为空";
            return false;
        }

        if (host.Length > 253)
        {
            error = "主机名过长";
            return false;
        }

        if (host.Contains(' ') || host.Contains('/') || host.Contains('\\'))
        {
            error = "主机地址包含非法字符";
            return false;
        }

        return true;
    }

    /// <summary>IPv6 作用域标识（%接口索引）只对链路本地/组播有意义，不能传播到对端当作全局地址。</summary>
    public static bool HasScopeId(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0;
}
