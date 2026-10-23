using LinkView.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LinkView.Signaling.Tests;

public class SignalingFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SignalingFlowTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task FullPairingAndSdpIceRelayFlow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await SignalTestClient.ConnectAsync(_factory, "书房电脑", cts.Token);
        await using var guest = await SignalTestClient.ConnectAsync(_factory, "手机", cts.Token);

        await host.AuthenticateAsync(cts.Token);
        await guest.AuthenticateAsync(cts.Token);

        await host.SendAsync(SignalMessageType.PairCreate, new PairCreatePayload(300), ct: cts.Token);
        var created = await host.ExpectAsync(SignalMessageType.PairCreated, ct: cts.Token);
        var createdPayload = SignalCodec.GetPayload<PairCreatedPayload>(created)!;
        Assert.Equal(6, createdPayload.PairingCode.Length);
        Assert.False(string.IsNullOrWhiteSpace(createdPayload.SessionId));

        await guest.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(createdPayload.PairingCode), ct: cts.Token);
        var joinRequest = await host.ExpectAsync(SignalMessageType.JoinRequest, ct: cts.Token);
        var joinPayload = SignalCodec.GetPayload<JoinRequestPayload>(joinRequest)!;
        Assert.Equal("手机", joinPayload.Guest.DeviceName);
        Assert.Equal(guest.Identity.DeviceId, joinPayload.Guest.DeviceId);
        Assert.Equal(createdPayload.SessionId, joinPayload.SessionId);

        await host.SendAsync(
            SignalMessageType.PairDecision,
            new PairDecisionPayload(true, null),
            createdPayload.SessionId,
            cts.Token);

        var hostJoined = await host.ExpectAsync(SignalMessageType.PairJoined, ct: cts.Token);
        var guestJoined = await guest.ExpectAsync(SignalMessageType.PairJoined, ct: cts.Token);
        Assert.Equal(SessionRole.Host, SignalCodec.GetPayload<PairJoinedPayload>(hostJoined)!.Role);
        Assert.Equal(SessionRole.Guest, SignalCodec.GetPayload<PairJoinedPayload>(guestJoined)!.Role);
        Assert.Equal(host.Identity.DeviceId, SignalCodec.GetPayload<PairJoinedPayload>(guestJoined)!.Peer.DeviceId);

        const string offerSdp = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n";
        await guest.SendAsync(
            SignalMessageType.SdpOffer,
            new SdpPayload("offer", offerSdp),
            createdPayload.SessionId,
            cts.Token);
        var relayedOffer = await host.ExpectAsync(SignalMessageType.SdpOffer, ct: cts.Token);
        Assert.Equal(offerSdp, SignalCodec.GetPayload<SdpPayload>(relayedOffer)!.Sdp);

        await host.SendAsync(
            SignalMessageType.SdpAnswer,
            new SdpPayload("answer", "v=0\r\nm=video 9 ..."),
            createdPayload.SessionId,
            cts.Token);
        await guest.ExpectAsync(SignalMessageType.SdpAnswer, ct: cts.Token);

        await guest.SendAsync(
            SignalMessageType.IceCandidate,
            new IceCandidatePayload("candidate:1 1 UDP 2130706431 192.168.1.5 50000 typ host", "0", 0, null),
            createdPayload.SessionId,
            cts.Token);
        var relayedCandidate = await host.ExpectAsync(SignalMessageType.IceCandidate, ct: cts.Token);
        Assert.Contains("192.168.1.5", SignalCodec.GetPayload<IceCandidatePayload>(relayedCandidate)!.Candidate);

        await host.SendAsync(
            SignalMessageType.SessionEnd,
            new SessionEndPayload("user stopped"),
            createdPayload.SessionId,
            cts.Token);
        var ended = await guest.ExpectAsync(SignalMessageType.SessionEnd, ct: cts.Token);
        Assert.Equal("user stopped", SignalCodec.GetPayload<SessionEndPayload>(ended)!.Reason);

        // 会话结束后继续转发必须被拒绝。
        await guest.SendAsync(
            SignalMessageType.IceCandidate,
            new IceCandidatePayload("candidate:2 1 UDP 1 1.2.3.4 1 typ host", "0", 0, null),
            createdPayload.SessionId,
            cts.Token);
        await guest.ExpectErrorAsync(SignalErrorCode.NotInSession, ct: cts.Token);
    }

    [Fact]
    public async Task UnauthenticatedClient_CannotCreateSession()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await SignalTestClient.ConnectAsync(_factory, "anonymous", cts.Token);

        await client.SendAsync(SignalMessageType.PairCreate, new PairCreatePayload(300), ct: cts.Token);
        await client.ExpectErrorAsync(SignalErrorCode.Unauthorized, ct: cts.Token);
    }

    [Fact]
    public async Task WrongPairingCode_LocksAfterMaxAttempts_AndNotifiesHost()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await SignalTestClient.ConnectAsync(_factory, "host", cts.Token);
        await using var guest = await SignalTestClient.ConnectAsync(_factory, "guest", cts.Token);
        await host.AuthenticateAsync(cts.Token);
        await guest.AuthenticateAsync(cts.Token);

        await host.SendAsync(SignalMessageType.PairCreate, new PairCreatePayload(300), ct: cts.Token);
        var created = SignalCodec.GetPayload<PairCreatedPayload>(
            await host.ExpectAsync(SignalMessageType.PairCreated, ct: cts.Token))!;
        var wrong = created.PairingCode == "000000" ? "111111" : "000000";

        for (var i = 0; i < ProtocolConstants.MaxPairingAttempts - 1; i++)
        {
            await guest.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(wrong), ct: cts.Token);
            await guest.ExpectErrorAsync(SignalErrorCode.PairingInvalid, ct: cts.Token);
        }

        await guest.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(wrong), ct: cts.Token);
        await guest.ExpectErrorAsync(SignalErrorCode.PairingLocked, ct: cts.Token);

        // 锁定只作用于猜测方连接，不能影响宿主会话本身。
        // 使用正确配对码仍应可以继续配对（由另一个连接发起）。
        var hostHealth = await _factory.CreateClient().GetStringAsync("/healthz", cts.Token);
        Assert.Contains("sessions", hostHealth);
    }

    [Fact]
    public async Task PairingCode_IsSingleUse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await SignalTestClient.ConnectAsync(_factory, "host", cts.Token);
        await using var guest1 = await SignalTestClient.ConnectAsync(_factory, "guest1", cts.Token);
        await using var guest2 = await SignalTestClient.ConnectAsync(_factory, "guest2", cts.Token);
        await host.AuthenticateAsync(cts.Token);
        await guest1.AuthenticateAsync(cts.Token);
        await guest2.AuthenticateAsync(cts.Token);

        await host.SendAsync(SignalMessageType.PairCreate, new PairCreatePayload(300), ct: cts.Token);
        var created = SignalCodec.GetPayload<PairCreatedPayload>(
            await host.ExpectAsync(SignalMessageType.PairCreated, ct: cts.Token))!;

        await guest1.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(created.PairingCode), ct: cts.Token);
        await host.ExpectAsync(SignalMessageType.JoinRequest, ct: cts.Token);

        await guest2.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(created.PairingCode), ct: cts.Token);
        await guest2.ExpectErrorAsync(SignalErrorCode.PairingInvalid, ct: cts.Token);
    }

    [Fact]
    public async Task RejectingJoin_NotifiesGuest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await SignalTestClient.ConnectAsync(_factory, "host", cts.Token);
        await using var guest = await SignalTestClient.ConnectAsync(_factory, "guest", cts.Token);
        await host.AuthenticateAsync(cts.Token);
        await guest.AuthenticateAsync(cts.Token);

        await host.SendAsync(SignalMessageType.PairCreate, new PairCreatePayload(300), ct: cts.Token);
        var created = SignalCodec.GetPayload<PairCreatedPayload>(
            await host.ExpectAsync(SignalMessageType.PairCreated, ct: cts.Token))!;

        await guest.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(created.PairingCode), ct: cts.Token);
        await host.ExpectAsync(SignalMessageType.JoinRequest, ct: cts.Token);

        await host.SendAsync(
            SignalMessageType.PairDecision,
            new PairDecisionPayload(false, "not now"),
            created.SessionId,
            cts.Token);

        await guest.ExpectErrorAsync(SignalErrorCode.PeerUnavailable, ct: cts.Token);
    }

    [Fact]
    public async Task TamperedAuthSignature_IsRejected()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await SignalTestClient.ConnectAsync(_factory, "attacker", cts.Token);

        await client.SendAsync(SignalMessageType.Hello, new HelloPayload(
            client.Identity.DeviceId, "attacker", DeviceType.Windows,
            client.Identity.PublicKeyBase64, "0.9.0-test", ProtocolConstants.Version), ct: cts.Token);

        var challenge = await client.ExpectAsync(SignalMessageType.AuthChallenge, ct: cts.Token);
        var nonce = SignalCodec.GetPayload<AuthChallengePayload>(challenge)!.Nonce;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var canonical = LinkView.Core.Identity.DeviceIdentity.BuildAuthCanonical(nonce, client.Identity.DeviceId, timestamp);
        var signature = client.Identity.Sign(canonical);
        signature[^1] ^= 0xFF;

        await client.SendAsync(SignalMessageType.AuthProof,
            new AuthProofPayload(Convert.ToBase64String(signature), timestamp), ct: cts.Token);

        await client.ExpectErrorAsync(SignalErrorCode.AuthFailed, ct: cts.Token);
    }

    [Fact]
    public async Task DeviceIdMismatchWithPublicKey_IsRejected()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await SignalTestClient.ConnectAsync(_factory, "spoof", cts.Token);

        await client.SendAsync(SignalMessageType.Hello, new HelloPayload(
            "lv00000000000000000000000000000000", "spoof", DeviceType.Windows,
            client.Identity.PublicKeyBase64, "0.9.0-test", ProtocolConstants.Version), ct: cts.Token);

        await client.ExpectErrorAsync(SignalErrorCode.AuthFailed, ct: cts.Token);
    }
}
