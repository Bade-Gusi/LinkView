using System.Net;
using System.Net.WebSockets;
using LinkView.Protocol;

namespace LinkView.Signaling;

/// <summary>
/// 一条已建立的 WebSocket 客户端连接及其认证状态。
/// 该类只保存协商所需的最少信息，不保存任何媒体数据。
/// </summary>
public sealed class ClientConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _rateTokens;

    public ClientConnection(WebSocket socket, IPAddress? remoteAddress, SignalingOptions options, ILogger logger)
    {
        _socket = socket;
        Id = Guid.NewGuid().ToString("N");
        RemoteAddress = NormalizeAddress(remoteAddress);
        LastActivityUtc = DateTimeOffset.UtcNow;
        _rateTokens = options.MaxMessagesPerSecondPerConnection;
        Logger = logger;
    }

    public string Id { get; }
    public string RemoteAddress { get; }
    public ILogger Logger { get; }
    public DateTimeOffset LastActivityUtc { get; set; }
    public string? DeviceId { get; private set; }
    public DeviceSummary? Device { get; private set; }
    public byte[]? PublicKeySpki { get; private set; }
    public DeviceSummary? PendingDevice { get; set; }
    public byte[]? PendingPublicKeySpki { get; set; }
    public bool IsAuthenticated { get; private set; }
    public string? PendingNonce { get; set; }
    public DateTimeOffset PendingNonceExpiresUtc { get; set; }
    public string? SessionId { get; set; }
    public string? PendingJoinSessionId { get; set; }
    public bool CloseRequested { get; internal set; }
    public int FailedPairingAttempts { get; set; }
    public DateTimeOffset PairingBlockedUntilUtc { get; set; }

    /// <summary>序列化同一连接上的消息发送，避免多线程并发写 WebSocket。</summary>
    public async Task SendAsync(SignalEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = SignalCodec.SerializeToUtf8(envelope);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State != WebSocketState.Open)
            {
                return;
            }

            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task SendErrorAsync(SignalErrorCode code, string message, CancellationToken cancellationToken, int? retryAfterMs = null) =>
        SendAsync(SignalCodec.Create(SignalMessageType.Error, new ErrorPayload(code, message, retryAfterMs)), cancellationToken);

    public Task CloseAsync(string reason, CancellationToken cancellationToken) =>
        CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);

    public async Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
    {
        CloseRequested = true;
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await _socket.CloseAsync(status, reason, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                Logger.LogDebug("关闭 WebSocket 时发生异常（可忽略）：{Error}", ex.Message);
            }
        }
    }

    public void MarkAuthenticated(DeviceSummary device, byte[] publicKey)
    {
        Device = device;
        DeviceId = device.DeviceId;
        PublicKeySpki = publicKey;
        IsAuthenticated = true;
        PendingNonce = null;
    }

    /// <summary>令牌桶限流：每秒补充到上限，防止单连接消息洪泛。</summary>
    public bool TryConsumeRateToken(int maxPerSecond)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastRefillTicks);
        if (last == 0)
        {
            Interlocked.Exchange(ref _lastRefillTicks, nowTicks);
        }
        else
        {
            var elapsedSeconds = (nowTicks - last) / (double)TimeSpan.TicksPerSecond;
            if (elapsedSeconds > 0)
            {
                var replenish = (long)(elapsedSeconds * maxPerSecond);
                if (replenish > 0)
                {
                    var current = Interlocked.Read(ref _rateTokens);
                    Interlocked.Exchange(ref _rateTokens, Math.Min(maxPerSecond, current + replenish));
                    Interlocked.Exchange(ref _lastRefillTicks, nowTicks);
                }
            }
        }

        while (true)
        {
            var current = Interlocked.Read(ref _rateTokens);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _rateTokens, current - 1, current) == current)
            {
                return true;
            }
        }
    }

    private long _lastRefillTicks;

    private static string NormalizeAddress(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        try
        {
            _socket.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        await Task.CompletedTask;
    }
}
