using System.Net.WebSockets;
using System.Text;
using LinkView.Core.Identity;
using LinkView.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LinkView.Signaling.Tests;

/// <summary>集成测试用的真实 WebSocket 客户端，不使用任何模拟信令逻辑。</summary>
internal sealed class SignalTestClient : IAsyncDisposable
{
    private readonly WebSocket _socket;

    private SignalTestClient(WebSocket socket, DeviceIdentity identity, string name)
    {
        _socket = socket;
        Identity = identity;
        Name = name;
    }

    public DeviceIdentity Identity { get; }
    public string Name { get; }

    public static async Task<SignalTestClient> ConnectAsync(WebApplicationFactory<Program> factory, string name, CancellationToken ct = default)
    {
        var client = factory.Server.CreateWebSocketClient();
        var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), ct);
        return new SignalTestClient(socket, DeviceIdentity.CreateEphemeral(), name);
    }

    public async Task SendAsync<T>(SignalMessageType type, T payload, string? sessionId = null, CancellationToken ct = default)
    {
        var envelope = SignalCodec.Create(type, payload, sessionId);
        await SendRawAsync(SignalCodec.SerializeToUtf8(envelope), ct);
    }

    public async Task SendRawAsync(byte[] bytes, CancellationToken ct = default)
    {
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task<SignalEnvelope> ReceiveAsync(int timeoutMs = 5000, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(chunk), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException(
                    $"WebSocket closed while waiting for a message: {_socket.CloseStatus} {_socket.CloseStatusDescription}");
            }

            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (!SignalCodec.TryParse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), out var envelope, out var error))
        {
            throw new InvalidOperationException($"Invalid server message: {error}");
        }

        return envelope!;
    }

    public async Task<SignalEnvelope> ExpectAsync(SignalMessageType expected, int timeoutMs = 5000, CancellationToken ct = default)
    {
        var envelope = await ReceiveAsync(timeoutMs, ct);
        Assert.Equal(expected, envelope.Type);
        return envelope;
    }

    public async Task<ErrorPayload> ExpectErrorAsync(SignalErrorCode expected, int timeoutMs = 5000, CancellationToken ct = default)
    {
        var envelope = await ReceiveAsync(timeoutMs, ct);
        Assert.Equal(SignalMessageType.Error, envelope.Type);
        var payload = SignalCodec.GetPayload<ErrorPayload>(envelope);
        Assert.NotNull(payload);
        if (payload!.Code != expected)
        {
            throw new Xunit.Sdk.XunitException($"Expected {expected}, got {payload.Code}: {payload.Message}");
        }

        return payload;
    }

    public async Task AuthenticateAsync(CancellationToken ct = default)
    {
        await SendAsync(SignalMessageType.Hello, new HelloPayload(
            Identity.DeviceId, Name, DeviceType.Windows,
            Identity.PublicKeyBase64, "0.9.0-test", ProtocolConstants.Version), ct: ct);

        var challenge = await ExpectAsync(SignalMessageType.AuthChallenge, ct: ct);
        var payload = SignalCodec.GetPayload<AuthChallengePayload>(challenge)!;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var canonical = DeviceIdentity.BuildAuthCanonical(payload.Nonce, Identity.DeviceId, timestamp);
        var signature = Convert.ToBase64String(Identity.Sign(canonical));

        await SendAsync(SignalMessageType.AuthProof, new AuthProofPayload(signature, timestamp), ct: ct);
        var ok = await ExpectAsync(SignalMessageType.AuthOk, ct: ct);
        Assert.NotNull(SignalCodec.GetPayload<AuthOkPayload>(ok));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", cts.Token);
            }
        }
        catch (Exception) when (_socket.State is WebSocketState.Aborted or WebSocketState.Closed)
        {
        }
        finally
        {
            _socket.Dispose();
        }
    }
}
