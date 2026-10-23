using System.Text;
using System.Text.Json;
using LinkView.Protocol;

namespace LinkView.Protocol.Tests;

public class SignalCodecTests
{
    [Fact]
    public void RoundTrip_PreservesEnvelopeAndPayload()
    {
        var payload = new SdpPayload("offer", "v=0\r\n...");
        var envelope = SignalCodec.Create(SignalMessageType.SdpOffer, payload, "session-1");
        var bytes = SignalCodec.SerializeToUtf8(envelope);

        Assert.True(SignalCodec.TryParse(bytes, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(SignalMessageType.SdpOffer, parsed!.Type);
        Assert.Equal("session-1", parsed.SessionId);

        var decoded = SignalCodec.GetPayload<SdpPayload>(parsed);
        Assert.NotNull(decoded);
        Assert.Equal("offer", decoded!.SdpType);
        Assert.Equal(payload.Sdp, decoded.Sdp);
    }

    [Fact]
    public void Enum_SerializedAsCamelCaseString()
    {
        var envelope = SignalCodec.Create(SignalMessageType.PairJoin, new PairJoinPayload("123456"));
        var json = SignalCodec.Serialize(envelope);
        Assert.Contains("\"type\":\"pairJoin\"", json);
        Assert.DoesNotContain("\"Payload\":", json);
    }

    [Fact]
    public void UnsupportedVersion_IsRejected()
    {
        var envelope = SignalCodec.Create(SignalMessageType.Ping, new PingPayload(1));
        var json = SignalCodec.Serialize(envelope);
        var modified = json.Replace($"\"v\":{ProtocolConstants.Version}", "\"v\":999");
        var bytes = Encoding.UTF8.GetBytes(modified);

        Assert.False(SignalCodec.TryParse(bytes, out _, out var error));
        Assert.Contains("version", error);
    }

    [Fact]
    public void MalformedJson_DoesNotThrow()
    {
        Assert.False(SignalCodec.TryParse(Encoding.UTF8.GetBytes("{not json"), out var envelope, out var error));
        Assert.Null(envelope);
        Assert.NotNull(error);
    }

    [Fact]
    public void UnknownEnumValue_IsRejected()
    {
        var json = $$"""
        {"v":{{ProtocolConstants.Version}},"id":"abc","type":"notARealType","ts":1}
        """;
        Assert.False(SignalCodec.TryParse(Encoding.UTF8.GetBytes(json), out _, out _));
    }

    [Fact]
    public void MissingPayload_ReturnsDefault()
    {
        var json = $$"""
        {"v":{{ProtocolConstants.Version}},"id":"abc","type":"sessionEnd","ts":1}
        """;
        Assert.True(SignalCodec.TryParse(Encoding.UTF8.GetBytes(json), out var envelope, out _));
        Assert.Null(SignalCodec.GetPayload<SessionEndPayload>(envelope!));
    }

    [Fact]
    public void ErrorPayload_SerializesCodeAsString()
    {
        var envelope = SignalCodec.Create(SignalMessageType.Error, new ErrorPayload(SignalErrorCode.RateLimited, "slow down", 1000));
        var json = SignalCodec.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("rateLimited", doc.RootElement.GetProperty("payload").GetProperty("code").GetString());
    }
}
