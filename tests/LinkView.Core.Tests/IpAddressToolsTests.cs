using System.Net;
using LinkView.Core.Networking;

namespace LinkView.Core.Tests;

public class IpAddressToolsTests
{
    [Theory]
    [InlineData("10.1.2.3", IpScope.Private)]
    [InlineData("172.16.0.1", IpScope.Private)]
    [InlineData("172.31.255.255", IpScope.Private)]
    [InlineData("172.32.0.1", IpScope.GlobalUnicast)]
    [InlineData("192.168.1.1", IpScope.Private)]
    [InlineData("127.0.0.1", IpScope.Loopback)]
    [InlineData("169.254.3.4", IpScope.LinkLocal)]
    [InlineData("8.8.8.8", IpScope.GlobalUnicast)]
    [InlineData("224.0.0.1", IpScope.Multicast)]
    public void ClassifiesIPv4(string text, IpScope expected)
    {
        Assert.Equal(expected, IpAddressTools.GetScope(IPAddress.Parse(text)));
    }

    [Theory]
    [InlineData("2001:4860:4860::8888", IpScope.GlobalUnicast)]
    [InlineData("fd12:3456::1", IpScope.Private)]
    [InlineData("fe80::1", IpScope.LinkLocal)]
    [InlineData("::1", IpScope.Loopback)]
    [InlineData("ff02::1", IpScope.Multicast)]
    [InlineData("::ffff:192.168.0.10", IpScope.Private)]
    public void ClassifiesIPv6(string text, IpScope expected)
    {
        Assert.Equal(expected, IpAddressTools.GetScope(IPAddress.Parse(text)));
    }

    [Theory]
    [InlineData("192.168.0.10", "192.168.0.10:8080")]
    [InlineData("fe80::1", "[fe80::1]:8080")]
    [InlineData("2001:db8::1", "[2001:db8::1]:8080")]
    public void FormatsEndpointsWithRequiredBrackets(string host, string expected)
    {
        Assert.Equal(expected, IpAddressTools.FormatEndpoint(host, 8080));
    }

    [Theory]
    [InlineData("192.168.0.10", "192.168.0.10", 0)]
    [InlineData("192.168.0.10:9000", "192.168.0.10", 9000)]
    [InlineData("[2001:db8::1]:9000", "2001:db8::1", 9000)]
    [InlineData("[fe80::1%12]:9000", "fe80::1%12", 9000)]
    [InlineData("2001:db8::1", "2001:db8::1", 0)]
    [InlineData("example.com:443", "example.com", 443)]
    public void ParsesEndpoints(string input, string host, int port)
    {
        Assert.True(IpAddressTools.TryParseEndpoint(input, out var parsedHost, out var parsedPort, out var error), error);
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[2001:db8::1")]
    [InlineData("192.168.0.1:99999")]
    [InlineData("192.168.0.1:abc")]
    [InlineData("[2001:db8::1]extra")]
    [InlineData("host with space")]
    public void RejectsMalformedEndpoints(string input)
    {
        Assert.False(IpAddressTools.TryParseEndpoint(input, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void LinkLocalIPv6_NotUsableByDefault_ButAllowedExplicitly()
    {
        var ll = IPAddress.Parse("fe80::abcd");
        Assert.False(IpAddressTools.IsUsableForConnection(ll));
        Assert.True(IpAddressTools.IsUsableForConnection(ll, allowLinkLocal: true));
    }

    [Fact]
    public void LoopbackAndMulticast_AreNeverUsableForPeerConnections()
    {
        Assert.False(IpAddressTools.IsUsableForConnection(IPAddress.Loopback));
        Assert.False(IpAddressTools.IsUsableForConnection(IPAddress.Parse("ff02::1")));
    }
}
