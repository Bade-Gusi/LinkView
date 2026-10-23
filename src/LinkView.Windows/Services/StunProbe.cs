using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LinkView.Core.Networking;

namespace LinkView.Windows.Services;

public sealed record StunProbeResult(
    bool Success,
    string Server,
    long? RoundTripMs,
    string? MappedAddress,
    string? LocalAddress,
    string? Error);

/// <summary>
/// 真实的 STUN Binding 请求（RFC 5389）。用于诊断 UDP 可达性与 NAT 映射，
/// 不能据此断言 WebRTC 一定可以建连。
/// </summary>
public sealed class StunProbe
{
    private const uint MagicCookie = 0x2112A442;

    public static bool TryParseServer(string input, out string host, out int port, out string? error)
    {
        host = string.Empty;
        port = 3478;
        error = null;

        var text = input.Trim();
        if (text.StartsWith("stun:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[5..];
        }
        else if (text.StartsWith("stuns:", StringComparison.OrdinalIgnoreCase))
        {
            error = "暂不支持基于 TCP/TLS 的 STUN 探测";
            return false;
        }

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
                if (!int.TryParse(rest[1..], out port) || port is < 1 or > 65535)
                {
                    error = "STUN 端口不合法";
                    return false;
                }
            }

            return true;
        }

        if (text.Count(c => c == ':') == 1)
        {
            var parts = text.Split(':');
            host = parts[0];
            if (!int.TryParse(parts[1], out port) || port is < 1 or > 65535)
            {
                error = "STUN 端口不合法";
                return false;
            }
        }
        else
        {
            host = text;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "STUN 服务器地址为空";
            return false;
        }

        return true;
    }

    public async Task<StunProbeResult> ProbeAsync(string server, CancellationToken cancellationToken)
    {
        var display = server;
        if (!TryParseServer(server, out var host, out var port, out var parseError))
        {
            return new StunProbeResult(false, display, null, null, null, parseError);
        }

        IPAddress[] addresses;
        try
        {
            if (IPAddress.TryParse(host, out var literal))
            {
                addresses = [literal];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return new StunProbeResult(false, display, null, null, null, "DNS 解析失败：" + ex.Message);
        }

        if (addresses.Length == 0)
        {
            return new StunProbeResult(false, display, null, null, null, "域名没有可用地址");
        }

        Exception? lastError = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            try
            {
                var result = await ProbeAddressAsync(address, port, display, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    return result;
                }

                lastError = new SocketException((int)SocketError.TimedOut);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                lastError = ex;
            }
        }

        return new StunProbeResult(false, display, null, null, null,
            lastError?.Message ?? "连接超时");
    }

    private static async Task<StunProbeResult> ProbeAddressAsync(
        IPAddress address,
        int port,
        string display,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(address.AddressFamily == AddressFamily.InterNetwork
            ? IPAddress.Any
            : IPAddress.IPv6Any, 0));

        var transactionId = RandomNumberGenerator.GetBytes(12);
        var request = new byte[20];
        request[0] = 0x00;
        request[1] = 0x01;
        request[4] = 0x21;
        request[5] = 0x12;
        request[6] = 0xA4;
        request[7] = 0x42;
        transactionId.CopyTo(request, 8);

        var stopwatch = Stopwatch.StartNew();
        await socket.SendToAsync(request, SocketFlags.None, new IPEndPoint(address, port), cancellationToken)
            .ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        var response = new byte[1500];
        EndPoint remote = new IPEndPoint(address.AddressFamily == AddressFamily.InterNetwork
            ? IPAddress.Any
            : IPAddress.IPv6Any, 0);

        var received = await socket.ReceiveFromAsync(response, SocketFlags.None, remote, timeout.Token)
            .ConfigureAwait(false);
        stopwatch.Stop();

        if (received.ReceivedBytes < 20 || response[0] != 0x01 || response[1] != 0x01)
        {
            return new StunProbeResult(false, display, null, null, null, "收到了非 STUN Binding 响应");
        }

        if (!response.AsSpan(8, 12).SequenceEqual(transactionId))
        {
            return new StunProbeResult(false, display, null, null, null, "STUN 事务 ID 不匹配");
        }

        var length = (response[2] << 8) | response[3];
        var offset = 20;
        var end = Math.Min(response.Length, 20 + length);
        while (offset + 4 <= end)
        {
            var attrType = (response[offset] << 8) | response[offset + 1];
            var attrLength = (response[offset + 2] << 8) | response[offset + 3];
            var valueOffset = offset + 4;
            if (valueOffset + attrLength > end)
            {
                break;
            }

            if (attrType == 0x0020 && attrLength >= 8)
            {
                var family = response[valueOffset + 1];
                var xorPort = (response[valueOffset + 2] << 8) | response[valueOffset + 3];
                var mappedPort = xorPort ^ (ushort)(MagicCookie >> 16);

                string mapped;
                if (family == 0x01)
                {
                    var ip = new byte[4];
                    for (var i = 0; i < 4; i++)
                    {
                        ip[i] = (byte)(response[valueOffset + 4 + i] ^ (byte)(MagicCookie >> (24 - i * 8)));
                    }

                    mapped = IpAddressTools.FormatEndpoint(new IPAddress(ip).ToString(), mappedPort);
                }
                else if (family == 0x02)
                {
                    var ip = new byte[16];
                    for (var i = 0; i < 16; i++)
                    {
                        var mask = i < 4
                            ? (byte)(MagicCookie >> (24 - i * 8))
                            : transactionId[i - 4];
                        ip[i] = (byte)(response[valueOffset + 4 + i] ^ mask);
                    }

                    mapped = IpAddressTools.FormatEndpoint(new IPAddress(ip).ToString(), mappedPort);
                }
                else
                {
                    mapped = "未知地址族";
                }

                var local = socket.LocalEndPoint is IPEndPoint localEp
                    ? IpAddressTools.FormatEndpoint(localEp)
                    : null;
                return new StunProbeResult(true, display, stopwatch.ElapsedMilliseconds, mapped, local, null);
            }

            offset = valueOffset + ((attrLength + 3) & ~3);
        }

        return new StunProbeResult(true, display, stopwatch.ElapsedMilliseconds, null,
            (socket.LocalEndPoint as IPEndPoint)?.ToString(), "响应中没有 XOR-MAPPED-ADDRESS");
    }
}
