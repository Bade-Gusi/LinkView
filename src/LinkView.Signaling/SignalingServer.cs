using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using LinkView.Core.Identity;
using LinkView.Core.Networking;
using LinkView.Protocol;

namespace LinkView.Signaling;

/// <summary>
/// 轻量信令服务核心。职责仅限于：认证设备、创建临时配对会话、在会话双方之间转发 SDP/ICE。
/// 服务端不接收、不转发、不存储任何屏幕画面或音频数据。
/// </summary>
public sealed class SignalingServer
{
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private readonly ConcurrentDictionary<string, PairingSession> _sessions = new();
    private readonly ConcurrentDictionary<string, int> _connectionsPerIp = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IpJoinAttempts> _joinAttemptsPerIp = new(StringComparer.Ordinal);
    private readonly SignalingOptions _options;
    private readonly ILogger<SignalingServer> _logger;

    public SignalingServer(
        Microsoft.Extensions.Options.IOptions<SignalingOptions> options,
        ILogger<SignalingServer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public int ConnectionCount => _connections.Count;
    public int SessionCount => _sessions.Count;

    public bool TryRegister(ClientConnection connection, out string reason)
    {
        reason = string.Empty;
        if (_connections.Count >= _options.MaxConnections)
        {
            reason = "server connection limit reached";
            return false;
        }

        var ipCount = _connectionsPerIp.AddOrUpdate(connection.RemoteAddress, 1, (_, v) => v + 1);
        if (ipCount > 30)
        {
            _connectionsPerIp.AddOrUpdate(connection.RemoteAddress, 0, (_, v) => Math.Max(0, v - 1));
            reason = "too many connections from this address";
            return false;
        }

        if (!_connections.TryAdd(connection.Id, connection))
        {
            reason = "connection registration failed";
            return false;
        }

        _logger.LogInformation(
            "signaling_connection_opened {ConnectionId} {RemoteAddress} total={Total}",
            connection.Id, connection.RemoteAddress, _connections.Count);
        return true;
    }

    public async Task HandleMessageAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        connection.LastActivityUtc = DateTimeOffset.UtcNow;

        if (!connection.TryConsumeRateToken(_options.MaxMessagesPerSecondPerConnection))
        {
            _logger.LogWarning("signaling_rate_limited {ConnectionId}", connection.Id);
            await SafeSendErrorAsync(connection, SignalErrorCode.RateLimited, "消息发送过快", ct, 1000);
            return;
        }

        try
        {
            switch (envelope.Type)
            {
                case SignalMessageType.Hello:
                    await HandleHelloAsync(connection, envelope, ct);
                    break;
                case SignalMessageType.AuthProof:
                    await HandleAuthProofAsync(connection, envelope, ct);
                    break;
                case SignalMessageType.PairCreate:
                    await RequireAuthAsync(connection, envelope, ct, HandlePairCreateAsync);
                    break;
                case SignalMessageType.PairJoin:
                    await RequireAuthAsync(connection, envelope, ct, HandlePairJoinAsync);
                    break;
                case SignalMessageType.PairDecision:
                    await RequireAuthAsync(connection, envelope, ct, HandlePairDecisionAsync);
                    break;
                case SignalMessageType.SdpOffer:
                case SignalMessageType.SdpAnswer:
                case SignalMessageType.IceCandidate:
                case SignalMessageType.SessionEnd:
                    await RequireAuthAsync(connection, envelope, ct, HandleRelayAsync);
                    break;
                case SignalMessageType.Ping:
                    var ping = SignalCodec.GetPayload<PingPayload>(envelope);
                    await connection.SendAsync(
                        SignalCodec.Create(SignalMessageType.Pong,
                            new PongPayload(ping?.ClientTimeMs ?? 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())),
                        ct);
                    break;
                default:
                    await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "不支持的消息类型", ct);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "signaling_message_failed {ConnectionId} {MessageType}", connection.Id, envelope.Type);
            await SafeSendErrorAsync(connection, SignalErrorCode.Internal, "服务端处理消息失败", CancellationToken.None);
        }
    }

    private async Task RequireAuthAsync(
        ClientConnection connection,
        SignalEnvelope envelope,
        CancellationToken ct,
        Func<ClientConnection, SignalEnvelope, CancellationToken, Task> handler)
    {
        if (!connection.IsAuthenticated)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.Unauthorized, "请先完成身份认证", ct);
            return;
        }

        await handler(connection, envelope, ct);
    }

    private async Task HandleHelloAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        if (connection.IsAuthenticated)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "连接已经完成认证", ct);
            return;
        }

        var hello = SignalCodec.GetPayload<HelloPayload>(envelope);
        if (hello is null || hello.ProtocolVersion != ProtocolConstants.Version)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.UnsupportedVersion, "协议版本不兼容", ct);
            await connection.CloseAsync("protocol version mismatch", ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(hello.DeviceName) || hello.DeviceName.Length > 64 ||
            string.IsNullOrWhiteSpace(hello.AppVersion) || hello.AppVersion.Length > 32)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "设备信息不合法", ct);
            return;
        }

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(hello.PublicKey);
        }
        catch (FormatException)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "公钥格式非法", ct);
            return;
        }

        if (spki.Length is < 64 or > 2048)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "公钥长度非法", ct);
            return;
        }

        var deviceId = DeviceIdentity.ComputeDeviceId(spki);
        if (!string.Equals(deviceId, hello.DeviceId, StringComparison.Ordinal))
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "设备 ID 与公钥不匹配", ct);
            return;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out _);
        }
        catch (CryptographicException)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "无法解析设备公钥", ct);
            return;
        }

        connection.PendingDevice = new DeviceSummary(
            deviceId, hello.DeviceName.Trim(), hello.DeviceType, hello.AppVersion, connection.RemoteAddress);
        connection.PendingPublicKeySpki = spki;
        connection.PendingNonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        connection.PendingNonceExpiresUtc = DateTimeOffset.UtcNow + ProtocolConstants.AuthChallengeLifetime;

        await connection.SendAsync(
            SignalCodec.Create(SignalMessageType.AuthChallenge,
                new AuthChallengePayload(connection.PendingNonce, connection.PendingNonceExpiresUtc.ToUnixTimeMilliseconds())),
            ct);
    }

    private async Task HandleAuthProofAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        if (connection.IsAuthenticated)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "连接已经完成认证", ct);
            return;
        }

        var pendingDevice = connection.PendingDevice;
        var pendingKey = connection.PendingPublicKeySpki;
        var nonce = connection.PendingNonce;
        if (pendingDevice is null || pendingKey is null || nonce is null ||
            DateTimeOffset.UtcNow > connection.PendingNonceExpiresUtc)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthExpired, "认证挑战已过期，请重新发起认证", ct);
            return;
        }

        // 每个挑战只能使用一次。
        connection.PendingNonce = null;

        var proof = SignalCodec.GetPayload<AuthProofPayload>(envelope);
        if (proof is null)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "认证消息格式非法", ct);
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - proof.ClientTimestampUnixMs) > (long)ProtocolConstants.AuthClockSkew.TotalMilliseconds)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthExpired, "客户端时间偏差过大", ct);
            return;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(proof.Signature);
        }
        catch (FormatException)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "签名格式非法", ct);
            return;
        }

        var canonical = DeviceIdentity.BuildAuthCanonical(nonce, pendingDevice.DeviceId, proof.ClientTimestampUnixMs);
        if (!DeviceIdentity.Verify(pendingKey, canonical, signature))
        {
            _logger.LogWarning("signaling_auth_failed {ConnectionId} {RemoteAddress}", connection.Id, connection.RemoteAddress);
            await SafeSendErrorAsync(connection, SignalErrorCode.AuthFailed, "签名验证失败", ct);
            return;
        }

        connection.MarkAuthenticated(pendingDevice with { ObservedAddress = connection.RemoteAddress }, pendingKey);
        connection.PendingDevice = null;
        connection.PendingPublicKeySpki = null;
        _logger.LogInformation(
            "signaling_device_authenticated {ConnectionId} {DeviceId} {DeviceType}",
            connection.Id, pendingDevice.DeviceId, pendingDevice.DeviceType);

        await connection.SendAsync(
            SignalCodec.Create(SignalMessageType.AuthOk,
                new AuthOkPayload(connection.Device!, HeartbeatSeconds: 30)),
            ct);
    }

    private async Task HandlePairCreateAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        if (connection.SessionId is not null)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.SessionFull, "当前连接已有进行中的会话", ct);
            return;
        }

        var deviceId = connection.DeviceId!;
        var hosted = _sessions.Values.Count(s => s.Host.DeviceId == deviceId && s.Phase != PairingPhase.Closed);
        if (hosted >= _options.MaxSessionsPerDevice)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.SessionFull, "此设备创建的会话数已达上限", ct);
            return;
        }

        if (_sessions.Count >= _options.MaxSessions)
        {
            _logger.LogWarning("signaling_capacity_reached {Sessions}", _sessions.Count);
            await SafeSendErrorAsync(connection, SignalErrorCode.Internal, "信令服务繁忙，请稍后重试", ct, 2000);
            return;
        }

        var request = SignalCodec.GetPayload<PairCreatePayload>(envelope);
        var ttl = Math.Clamp(request?.TtlSeconds ?? (int)ProtocolConstants.DefaultPairingLifetime.TotalSeconds, 60, 3600);

        string code;
        var attempts = 0;
        var collision = false;
        do
        {
            code = PairingCode.Generate();
            attempts++;
            collision = _sessions.Values.Any(s =>
                s.Phase == PairingPhase.WaitingForGuest &&
                PairingCode.Verify(code, s.CodeSalt, s.CodeHash));
        } while (collision && attempts < 20);

        if (collision)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.Internal, "无法分配配对码，请稍后重试", ct);
            return;
        }

        var (salt, hash) = PairingCode.Hash(code);
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new PairingSession
        {
            Id = sessionId,
            Host = connection,
            Code = code,
            CodeSalt = salt,
            CodeHash = hash,
            PairingExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(ttl),
            NegotiationDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(_options.NegotiationTimeoutSeconds)
        };

        if (!_sessions.TryAdd(sessionId, session))
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.Internal, "创建会话失败", ct);
            return;
        }

        connection.SessionId = sessionId;

        _logger.LogInformation(
            "signaling_pair_created {SessionId} {HostDeviceId} ttlSeconds={Ttl}",
            sessionId, deviceId, ttl);

        await connection.SendAsync(
            SignalCodec.Create(SignalMessageType.PairCreated,
                new PairCreatedPayload(sessionId, code, session.PairingExpiresAtUtc.ToUnixTimeMilliseconds()),
                sessionId),
            ct);
    }

    private async Task HandlePairJoinAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        if (connection.SessionId is not null || connection.PendingJoinSessionId is not null)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.SessionFull, "当前连接已在会话中", ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (connection.PairingBlockedUntilUtc > now)
        {
            var retryMs = (int)Math.Ceiling((connection.PairingBlockedUntilUtc - now).TotalMilliseconds);
            await SafeSendErrorAsync(connection, SignalErrorCode.PairingLocked,
                "绑定尝试次数过多，请稍后再试", ct, retryMs);
            return;
        }

        if (IsIpBlocked(connection.RemoteAddress, now, out var ipRetryMs))
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.PairingLocked,
                "该地址的配对尝试过于频繁，请稍后再试", ct, ipRetryMs);
            return;
        }

        var request = SignalCodec.GetPayload<PairJoinPayload>(envelope);
        if (request is null || !PairingCode.IsWellFormed(request.PairingCode))
        {
            await RegisterJoinFailureAsync(connection, ct);
            return;
        }

        // 配对码是唯一检索依据。必须遍历当前等待加入的会话并用恒定时间哈希校验，
        // 不能把用户提交的码直接当作字典键（那样错误码将永远查询不到，也无法累计失败次数）。
        PairingSession? matched = null;
        var matchCount = 0;
        foreach (var candidate in _sessions.Values)
        {
            if (candidate.Phase != PairingPhase.WaitingForGuest || now > candidate.PairingExpiresAtUtc)
            {
                continue;
            }

            if (PairingCode.Verify(request.PairingCode, candidate.CodeSalt, candidate.CodeHash))
            {
                matched = candidate;
                matchCount++;
            }
        }

        if (matched is null || matchCount != 1)
        {
            await RegisterJoinFailureAsync(connection, ct);
            return;
        }

        ClientConnection? host = null;
        DeviceSummary? guestSummary = null;
        SignalErrorCode? errorCode = null;
        string? errorMessage = null;

        lock (matched.SyncRoot)
        {
            if (matched.Phase != PairingPhase.WaitingForGuest ||
                DateTimeOffset.UtcNow > matched.PairingExpiresAtUtc)
            {
                errorCode = SignalErrorCode.PairingExpired;
                errorMessage = "配对码已失效";
            }
            else if (matched.Host.DeviceId == connection.DeviceId)
            {
                errorCode = SignalErrorCode.InvalidMessage;
                errorMessage = "不能加入自己创建的会话";
            }
            else
            {
                matched.Phase = PairingPhase.AwaitingHostConsent;
                matched.Guest = connection;
                matched.NegotiationDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(_options.NegotiationTimeoutSeconds);
                connection.PendingJoinSessionId = matched.Id;
                connection.FailedPairingAttempts = 0;
                host = matched.Host;
                guestSummary = new DeviceSummary(
                    connection.DeviceId!, connection.Device!.DeviceName, connection.Device.DeviceType,
                    connection.Device.AppVersion, connection.RemoteAddress);
            }
        }

        if (errorCode is not null)
        {
            await SafeSendErrorAsync(connection, errorCode.Value, errorMessage!, ct);
            return;
        }

        _logger.LogInformation(
            "signaling_join_requested {SessionId} {GuestDeviceId}",
            matched.Id, connection.DeviceId);

        await SafeSendAsync(host!,
            SignalCodec.Create(SignalMessageType.JoinRequest,
                new JoinRequestPayload(matched.Id, guestSummary!), matched.Id), ct);
    }

    /// <summary>记录一次配对失败。先做连接级冷却，再叠加 IP 级窗口限流。</summary>
    private async Task RegisterJoinFailureAsync(ClientConnection connection, CancellationToken ct)
    {
        connection.FailedPairingAttempts++;
        var now = DateTimeOffset.UtcNow;
        var ipState = _joinAttemptsPerIp.GetOrAdd(connection.RemoteAddress, _ => new IpJoinAttempts());
        var ipBlocked = false;
        lock (ipState.SyncRoot)
        {
            if (now - ipState.WindowStartUtc > ProtocolConstants.PairingFailureWindow)
            {
                ipState.WindowStartUtc = now;
                ipState.Failures = 0;
            }

            ipState.Failures++;
            if (ipState.Failures >= ProtocolConstants.MaxPairingFailuresPerIpWindow)
            {
                ipState.BlockedUntilUtc = now + ProtocolConstants.PairingIpBlockDuration;
                ipState.Failures = 0;
                ipState.WindowStartUtc = now;
                ipBlocked = true;
            }
        }

        if (connection.FailedPairingAttempts >= ProtocolConstants.MaxPairingAttempts)
        {
            connection.FailedPairingAttempts = 0;
            connection.PairingBlockedUntilUtc = now + ProtocolConstants.PairingLockoutDuration;
            _logger.LogWarning("signaling_pairing_connection_locked {ConnectionId}", connection.Id);
            await SafeSendErrorAsync(connection, SignalErrorCode.PairingLocked,
                "配对尝试次数过多，请稍后再试", ct,
                (int)ProtocolConstants.PairingLockoutDuration.TotalMilliseconds);
            return;
        }

        if (ipBlocked)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.PairingLocked,
                "该地址的配对尝试过于频繁，请稍后再试", ct,
                (int)ProtocolConstants.PairingIpBlockDuration.TotalMilliseconds);
            return;
        }

        await SafeSendErrorAsync(connection, SignalErrorCode.PairingInvalid,
            "配对码无效或已过期", ct);
    }

    private bool IsIpBlocked(string address, DateTimeOffset now, out int retryAfterMs)
    {
        retryAfterMs = 0;
        if (!_joinAttemptsPerIp.TryGetValue(address, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            if (state.BlockedUntilUtc > now)
            {
                retryAfterMs = (int)Math.Ceiling((state.BlockedUntilUtc - now).TotalMilliseconds);
                return true;
            }

            return false;
        }
    }

    private sealed class IpJoinAttempts
    {
        public object SyncRoot { get; } = new();
        public DateTimeOffset WindowStartUtc { get; set; } = DateTimeOffset.UtcNow;
        public int Failures { get; set; }
        public DateTimeOffset BlockedUntilUtc { get; set; }
    }

    private async Task HandlePairDecisionAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        if (connection.SessionId is null || !_sessions.TryGetValue(connection.SessionId, out var session))
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.SessionNotFound, "会话不存在", ct);
            return;
        }

        var decision = SignalCodec.GetPayload<PairDecisionPayload>(envelope);
        if (decision is null)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "确认消息格式非法", ct);
            return;
        }

        ClientConnection? guest = null;
        DeviceSummary? hostSummary = null;
        DeviceSummary? guestSummary = null;
        string? denialMessage = null;
        var accepted = false;
        var rejected = false;
        var expired = false;

        lock (session.SyncRoot)
        {
            if (!ReferenceEquals(session.Host, connection) || session.Phase != PairingPhase.AwaitingHostConsent ||
                session.Guest is null)
            {
                denialMessage = "当前会话不处于等待确认状态";
            }
            else if (DateTimeOffset.UtcNow > session.NegotiationDeadlineUtc)
            {
                denialMessage = "确认已超时";
                expired = true;
            }
            else if (!decision.Accept)
            {
                guest = session.Guest;
                session.Phase = PairingPhase.Closed;
                rejected = true;
            }
            else
            {
                session.Phase = PairingPhase.Joined;
                session.NegotiationDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(_options.NegotiationTimeoutSeconds);
                guest = session.Guest;
                guest.PendingJoinSessionId = null;
                guest.SessionId = session.Id;
                connection.SessionId = session.Id;
                hostSummary = new DeviceSummary(
                    connection.DeviceId!, connection.Device!.DeviceName, connection.Device.DeviceType,
                    connection.Device.AppVersion, connection.RemoteAddress);
                guestSummary = new DeviceSummary(
                    guest.DeviceId!, guest.Device!.DeviceName, guest.Device.DeviceType,
                    guest.Device.AppVersion, guest.RemoteAddress);
                accepted = true;
            }
        }

        if (denialMessage is not null)
        {
            if (expired)
            {
                RemoveSession(session, "consent timeout");
            }

            await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, denialMessage, ct);
            return;
        }

        if (rejected)
        {
            if (guest is not null)
            {
                guest.PendingJoinSessionId = null;
                await SafeSendErrorAsync(guest, SignalErrorCode.PeerUnavailable, "对方拒绝了连接请求", ct);
            }

            RemoveSession(session, "rejected");
            _logger.LogInformation("signaling_join_rejected {SessionId}", session.Id);
            return;
        }

        if (!accepted)
        {
            return;
        }

        _logger.LogInformation("signaling_session_joined {SessionId}", session.Id);

        await SafeSendAsync(connection,
            SignalCodec.Create(SignalMessageType.PairJoined,
                new PairJoinedPayload(session.Id, guestSummary!, SessionRole.Host), session.Id), ct);
        await SafeSendAsync(guest!,
            SignalCodec.Create(SignalMessageType.PairJoined,
                new PairJoinedPayload(session.Id, hostSummary!, SessionRole.Guest), session.Id), ct);
    }

    private async Task HandleRelayAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        if (connection.SessionId is null || !_sessions.TryGetValue(connection.SessionId, out var session) ||
            session.Phase != PairingPhase.Joined)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.NotInSession, "当前不在已建立的会话中", ct);
            return;
        }

        if (envelope.SessionId is not null && !string.Equals(envelope.SessionId, session.Id, StringComparison.Ordinal))
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.NotInSession, "会话标识不匹配", ct);
            return;
        }

        if (!session.IsParticipant(connection))
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.Unauthorized, "无权访问该会话", ct);
            return;
        }

        var peer = session.PeerOf(connection);
        if (peer is null || !peer.IsAuthenticated || peer.SessionId != session.Id)
        {
            await SafeSendErrorAsync(connection, SignalErrorCode.PeerUnavailable, "对方已离线", ct);
            return;
        }

        if (envelope.Type == SignalMessageType.SessionEnd)
        {
            var endPayload = SignalCodec.GetPayload<SessionEndPayload>(envelope);
            await SafeSendAsync(peer,
                SignalCodec.Create(SignalMessageType.SessionEnd,
                    new SessionEndPayload(endPayload?.Reason ?? "peer ended session"), session.Id), ct);
            RemoveSession(session, "ended by peer");
            _logger.LogInformation("signaling_session_ended {SessionId}", session.Id);
            return;
        }

        if (envelope.Type is SignalMessageType.SdpOffer or SignalMessageType.SdpAnswer)
        {
            var sdp = SignalCodec.GetPayload<SdpPayload>(envelope);
            if (sdp is null || string.IsNullOrWhiteSpace(sdp.Sdp) || sdp.Sdp.Length > 32_768 ||
                sdp.SdpType is not ("offer" or "answer") ||
                (envelope.Type == SignalMessageType.SdpOffer && sdp.SdpType != "offer") ||
                (envelope.Type == SignalMessageType.SdpAnswer && sdp.SdpType != "answer"))
            {
                await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "SDP 内容不合法", ct);
                return;
            }
        }

        if (envelope.Type == SignalMessageType.IceCandidate)
        {
            var candidate = SignalCodec.GetPayload<IceCandidatePayload>(envelope);
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Candidate) ||
                candidate.Candidate.Length > 2048)
            {
                await SafeSendErrorAsync(connection, SignalErrorCode.InvalidMessage, "ICE 候选内容不合法", ct);
                return;
            }
        }

        await SafeSendAsync(peer, envelope with { SessionId = session.Id }, ct);
    }

    public async Task HandleDisconnectAsync(ClientConnection connection)
    {
        _connections.TryRemove(connection.Id, out _);
        _connectionsPerIp.AddOrUpdate(connection.RemoteAddress, 0, (_, v) => Math.Max(0, v - 1));

        if (connection.SessionId is not null && _sessions.TryGetValue(connection.SessionId, out var session))
        {
            var peer = session.PeerOf(connection);
            RemoveSession(session, "disconnected");
            connection.SessionId = null;
            if (peer is not null)
            {
                peer.SessionId = null;
                await SafeSendAsync(peer,
                    SignalCodec.Create(SignalMessageType.PeerLeft, new PeerLeftPayload("对方连接已断开")),
                    CancellationToken.None);
            }
        }
        else if (connection.PendingJoinSessionId is not null &&
                 _sessions.TryGetValue(connection.PendingJoinSessionId, out var pendingSession))
        {
            var host = pendingSession.Host;
            RemoveSession(pendingSession, "guest disconnected");
            if (!ReferenceEquals(host, connection))
            {
                host.SessionId = null;
                await SafeSendAsync(host,
                    SignalCodec.Create(SignalMessageType.PeerLeft, new PeerLeftPayload("对方取消了连接")),
                    CancellationToken.None);
            }
        }

        _logger.LogInformation("signaling_connection_closed {ConnectionId} ", connection.Id);
    }

    /// <summary>由后台清理服务周期调用：处理超时配对、僵尸会话和心跳超时连接。</summary>
    public async Task CleanupAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var session in _sessions.Values.ToArray())
        {
            if (session.Phase is PairingPhase.WaitingForGuest or PairingPhase.AwaitingHostConsent)
            {
                var expired = now > session.PairingExpiresAtUtc || now > session.NegotiationDeadlineUtc;
                var hostClosed = session.Host.CloseRequested;
                var guestClosed = session.Guest is null || session.Guest.CloseRequested;
                if (expired || hostClosed || (session.Phase == PairingPhase.AwaitingHostConsent && guestClosed))
                {
                    var reason = expired ? "配对已超时" : "对方已断开";
                    var guest = session.Guest;
                    var host = session.Host;
                    RemoveSession(session, reason);
                    host.SessionId = null;
                    if (guest is not null)
                    {
                        guest.PendingJoinSessionId = null;
                        guest.SessionId = null;
                        await SafeSendAsync(guest,
                            SignalCodec.Create(SignalMessageType.SessionEnd, new SessionEndPayload(reason)), ct);
                    }

                    await SafeSendAsync(host,
                        SignalCodec.Create(SignalMessageType.SessionEnd, new SessionEndPayload(reason)), ct);
                }
            }
            else if (session.Phase == PairingPhase.Joined)
            {
                var tooOld = now - session.CreatedAtUtc > ProtocolConstants.SessionLifetime;
                if (tooOld || session.Host.CloseRequested || session.Guest?.CloseRequested == true)
                {
                    var guest = session.Guest;
                    RemoveSession(session, "session lifetime exceeded");
                    session.Host.SessionId = null;
                    if (guest is not null)
                    {
                        guest.SessionId = null;
                        await SafeSendAsync(guest,
                            SignalCodec.Create(SignalMessageType.SessionEnd, new SessionEndPayload("会话已结束")), ct);
                    }
                }
            }
        }

        foreach (var connection in _connections.Values.ToArray())
        {
            if ((now - connection.LastActivityUtc).TotalSeconds > _options.HeartbeatTimeoutSeconds)
            {
                _logger.LogInformation("signaling_heartbeat_timeout {ConnectionId}", connection.Id);
                connection.CloseRequested = true;
                await connection.CloseAsync("heartbeat timeout", ct);
                await HandleDisconnectAsync(connection);
            }
        }
    }

    private void RemoveSession(PairingSession session, string reason)
    {
        session.Phase = PairingPhase.Closed;
        _sessions.TryRemove(session.Id, out _);
        if (session.Host.SessionId == session.Id)
        {
            session.Host.SessionId = null;
        }

        if (session.Guest?.SessionId == session.Id)
        {
            session.Guest.SessionId = null;
        }

        if (session.Guest?.PendingJoinSessionId == session.Id)
        {
            session.Guest.PendingJoinSessionId = null;
        }

        _logger.LogInformation("signaling_session_removed {SessionId} reason={Reason}", session.Id, reason);
    }

    private async Task SafeSendAsync(ClientConnection connection, SignalEnvelope envelope, CancellationToken ct)
    {
        try
        {
            await connection.SendAsync(envelope, ct);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug("signaling_send_failed {ConnectionId} {Error}", connection.Id, ex.Message);
        }
    }

    private Task SafeSendErrorAsync(ClientConnection connection, SignalErrorCode code, string message, CancellationToken ct, int? retryAfterMs = null)
    {
        return SafeSendAsync(connection,
            SignalCodec.Create(SignalMessageType.Error, new ErrorPayload(code, message, retryAfterMs)), ct);
    }
}
