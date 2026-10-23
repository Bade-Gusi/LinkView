using System.Buffers;
using System.Net.WebSockets;
using LinkView.Core.Identity;
using LinkView.Protocol;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services;

public sealed class SignalingException : Exception
{
    public SignalingException(SignalErrorCode code, string message) : base(message) => Code = code;

    public SignalErrorCode Code { get; }
}

/// <summary>
/// 与 LinkView 信令服务的 WebSocket 客户端。负责设备身份挑战应答、消息收发与心跳，
/// 不处理任何媒体数据。
/// </summary>
public sealed class SignalingClient : IAsyncDisposable
{
    private readonly IdentityService _identity;
    private readonly ILogger<SignalingClient> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;

    public SignalingClient(IdentityService identity, ILogger<SignalingClient> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    public event EventHandler<SignalEnvelope>? MessageReceived;
    public event EventHandler<string?>? Closed;

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public string? SessionId { get; set; }
    public DeviceSummary? Self { get; private set; }
    public string ServerUri { get; private set; } = string.Empty;

    public async Task ConnectAsync(string serverUri, string deviceName, DeviceType deviceType, string appVersion,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(serverUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException("信令服务地址必须以 ws:// 或 wss:// 开头。", nameof(serverUri));
        }

        if (IsConnected)
        {
            return;
        }

        ServerUri = serverUri;
        _socket = new ClientWebSocket();
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await _socket.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or HttpRequestException)
        {
            _logger.LogWarning("signaling_connect_failed {Server} {Error}", Sanitize(serverUri), ex.Message);
            throw new SignalingException(SignalErrorCode.Internal, "无法连接信令服务：" + ex.Message);
        }

        _cts = new CancellationTokenSource();
        try
        {
            await AuthenticateAsync(deviceName, deviceType, appVersion, connectTimeout.Token).ConfigureAwait(false);
        }
        catch
        {
            await CloseSocketAsync().ConfigureAwait(false);
            throw;
        }

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        _logger.LogInformation("signaling_connected {Server} {DeviceId}", Sanitize(serverUri), _identity.Identity.DeviceId);
    }

    private async Task AuthenticateAsync(string deviceName, DeviceType deviceType, string appVersion, CancellationToken ct)
    {
        await SendAsync(SignalMessageType.Hello, new HelloPayload(
            _identity.Identity.DeviceId,
            deviceName,
            deviceType,
            _identity.Identity.PublicKeyBase64,
            appVersion,
            ProtocolConstants.Version), ct: ct).ConfigureAwait(false);

        SignalEnvelope challenge;
        while (true)
        {
            challenge = await ReceiveEnvelopeAsync(ct).ConfigureAwait(false);
            if (challenge.Type == SignalMessageType.AuthChallenge)
            {
                break;
            }

            if (challenge.Type == SignalMessageType.Error)
            {
                var error = SignalCodec.GetPayload<ErrorPayload>(challenge);
                throw new SignalingException(error?.Code ?? SignalErrorCode.AuthFailed,
                    error?.Message ?? "信令服务拒绝了连接");
            }
        }

        var payload = SignalCodec.GetPayload<AuthChallengePayload>(challenge)
                      ?? throw new SignalingException(SignalErrorCode.AuthFailed, "认证挑战格式非法");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var canonical = DeviceIdentity.BuildAuthCanonical(payload.Nonce, _identity.Identity.DeviceId, timestamp);
        var signature = Convert.ToBase64String(_identity.Identity.Sign(canonical));

        await SendAsync(SignalMessageType.AuthProof, new AuthProofPayload(signature, timestamp), ct: ct)
            .ConfigureAwait(false);

        while (true)
        {
            var response = await ReceiveEnvelopeAsync(ct).ConfigureAwait(false);
            if (response.Type == SignalMessageType.AuthOk)
            {
                Self = SignalCodec.GetPayload<AuthOkPayload>(response)?.Self;
                break;
            }

            if (response.Type == SignalMessageType.Error)
            {
                var error = SignalCodec.GetPayload<ErrorPayload>(response);
                throw new SignalingException(error?.Code ?? SignalErrorCode.AuthFailed,
                    error?.Message ?? "身份认证失败");
            }
        }
    }

    public Task SendAsync<T>(SignalMessageType type, T payload, string? sessionId = null, CancellationToken ct = default)
    {
        var envelope = SignalCodec.Create(type, payload, sessionId);
        return SendEnvelopeAsync(envelope, ct);
    }

    public async Task SendEnvelopeAsync(SignalEnvelope envelope, CancellationToken ct = default)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new SignalingException(SignalErrorCode.Internal, "信令连接已断开");
        }

        var bytes = SignalCodec.SerializeToUtf8(envelope);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        string? closeReason = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var envelope = await ReceiveEnvelopeAsync(ct).ConfigureAwait(false);
                if (envelope.Type == SignalMessageType.Ping)
                {
                    var ping = SignalCodec.GetPayload<PingPayload>(envelope);
                    await SendAsync(SignalMessageType.Pong,
                        new PongPayload(ping?.ClientTimeMs ?? 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                        ct: ct).ConfigureAwait(false);
                    continue;
                }

                MessageReceived?.Invoke(this, envelope);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is WebSocketException or SignalingException or ObjectDisposedException)
        {
            closeReason = ex.Message;
            _logger.LogInformation("signaling_receive_ended {Reason}", ex.Message);
        }
        finally
        {
            Closed?.Invoke(this, closeReason);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                if (IsConnected)
                {
                    await SendAsync(SignalMessageType.Ping,
                        new PingPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct: ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is WebSocketException or SignalingException)
            {
                break;
            }
        }
    }

    private async Task<SignalEnvelope> ReceiveEnvelopeAsync(CancellationToken ct)
    {
        var socket = _socket ?? throw new SignalingException(SignalErrorCode.Internal, "信令连接已断开");
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        using var message = new MemoryStream(8192);
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    var reason = socket.CloseStatusDescription ?? "信令服务关闭了连接";
                    throw new SignalingException(SignalErrorCode.Internal, reason);
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > ProtocolConstants.MaxMessageBytes)
                {
                    throw new SignalingException(SignalErrorCode.PayloadTooLarge, "信令消息超过大小限制");
                }
            } while (!result.EndOfMessage);

            if (!SignalCodec.TryParse(message.GetBuffer().AsSpan(0, (int)message.Length), out var envelope, out var error))
            {
                throw new SignalingException(SignalErrorCode.InvalidMessage, "信令消息格式非法：" + error);
            }

            return envelope!;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task CloseSocketAsync()
    {
        var socket = _socket;
        _socket = null;
        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            socket.Dispose();
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        await CloseSocketAsync().ConfigureAwait(false);
        if (_receiveTask is not null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _receiveTask = null;
        }

        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _heartbeatTask = null;
        }

        _cts?.Dispose();
        _cts = null;
        SessionId = null;
    }

    private static string Sanitize(string uri) => uri;

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
