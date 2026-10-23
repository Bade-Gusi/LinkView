using LinkView.Core.Networking;
using System.Threading.Channels;
using LinkView.Core.Errors;
using LinkView.Core.Identity;
using LinkView.Core.Sessions;
using LinkView.Core.Settings;
using LinkView.Protocol;
using LinkView.Windows.Media;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace LinkView.Windows.Services;

public sealed record PairingInfo(string SessionId, string Code, DateTimeOffset ExpiresAt);

public sealed record LiveStats(
    ShareSessionState State,
    string? PeerName,
    SessionRole? Role,
    TimeSpan Duration,
    int SentFps,
    double SentKbps,
    int ReceivedFps,
    double ReceivedKbps,
    double EncodeMs,
    double DecodeMs,
    int SendWidth,
    int SendHeight,
    int ReceiveWidth,
    int ReceiveHeight,
    string Codec,
    string ConnectionState,
    string IceState,
    string PathLabel,
    long Reconnects,
    long Freezes,
    bool Sharing,
    DateTimeOffset? LastRemoteVideoAt,
    string? CaptureSourceName);

/// <summary>
/// 会话编排层：连接信令、完成配对、驱动 WebRTC、控制采集与共享。
/// 所有状态变化以 ShareSessionStateMachine 为准。
/// </summary>
public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly SettingsService _settings;
    private readonly IdentityService _identity;
    private readonly ScreenCaptureService _capture;
    private readonly SecretStore _secrets;
    private readonly NetworkInfoService _networkInfo;
    private readonly ConnectionHistoryService _history;
    private readonly DeviceStore _devices;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionCoordinator> _logger;
    private readonly ShareSessionStateMachine _state = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _statsSync = new();
    private readonly Channel<SignalEnvelope> _signalQueue;
    private SignalingClient? _signaling;
    private WebRtcSession? _peer;
    private CancellationTokenSource? _sessionCts;
    private Task? _signalProcessor;
    private TaskCompletionSource<bool>? _pairCreatedTcs;
    private TaskCompletionSource<bool>? _joinedTcs;
    private PairingInfo? _pairing;
    private DeviceSummary? _peerDevice;
    private SessionRole? _role;
    private string? _sessionId;
    private ConnectionRecord? _historyRecord;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _lastRemoteVideoAt;
    private DateTimeOffset? _lastLocalFrameAt;
    private DateTimeOffset? _lastKeyFrameAt;
    private long _reconnects;
    private long _freezes;
    private bool _sharing;
    private long _lastSentFrames;
    private long _lastSentBytes;
    private long _lastRecvFrames;
    private long _lastRecvBytes;
    private DateTime _lastRateSample = DateTime.UtcNow;
    private double _sentFps;
    private double _sentKbps;
    private double _recvFps;
    private double _recvKbps;
    private int _reconnectAttempts;
    private CancellationTokenSource? _reconnectCts;
    private CaptureSource? _captureSource;
    private bool _paused;
    private string? _pendingRemoteOffer;
    private readonly List<IceCandidatePayload> _pendingCandidates = [];

    public SessionCoordinator(
        SettingsService settings,
        IdentityService identity,
        ScreenCaptureService capture,
        SecretStore secrets,
        NetworkInfoService networkInfo,
        ConnectionHistoryService history,
        DeviceStore devices,
        ILoggerFactory loggerFactory,
        ILogger<SessionCoordinator> logger)
    {
        _settings = settings;
        _identity = identity;
        _capture = capture;
        _secrets = secrets;
        _networkInfo = networkInfo;
        _history = history;
        _devices = devices;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _signalQueue = Channel.CreateUnbounded<SignalEnvelope>(new UnboundedChannelOptions { SingleReader = true });
        _state.StateChanged += (_, change) =>
        {
            _logger.LogInformation("session_state {From} -> {To} ({Reason})", change.From, change.To, change.Reason);
            StateChanged?.Invoke(this, change);
        };
    }

    public event EventHandler<SessionStateChange>? StateChanged;
    public event EventHandler<LinkViewError>? ErrorOccurred;
    public event EventHandler<PairingInfo>? PairingCreated;
    public event EventHandler<DeviceSummary>? IncomingJoinRequest;
    public event EventHandler<(DeviceSummary Peer, SessionRole Role)>? PeerJoined;
    public event EventHandler<string>? PeerLeft;
    public event EventHandler<CapturedFrame>? PreviewFrame;
    public event EventHandler<DecodedFrame>? RemoteFrame;
    public event EventHandler<string>? Notice;

    public ShareSessionState State => _state.State;
    public DeviceSummary? PeerDevice => _peerDevice;
    public SessionRole? Role => _role;
    public PairingInfo? CurrentPairing => _pairing;
    public bool IsSharing => _sharing;
    public bool IsCapturing => _capture.IsCapturing;
    public string? SessionId => _sessionId;

    public async Task<PairingInfo> CreatePairingAsync(CancellationToken ct)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state.State is ShareSessionState.Pairing or ShareSessionState.AwaitingConsent or
                ShareSessionState.Negotiating or ShareSessionState.Connecting or ShareSessionState.Connected or
                ShareSessionState.Sharing or ShareSessionState.Reconnecting)
            {
                throw new InvalidOperationException("已有进行中的连接流程。");
            }

            ResetForNewSession();
            _state.Transition(ShareSessionState.Pairing, "创建配对码");
            var settings = _settings.Current;
            await EnsureSignalingAsync(settings, ct).ConfigureAwait(false);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pairCreatedTcs = tcs;
            await _signaling!.SendAsync(SignalMessageType.PairCreate,
                new PairCreatePayload(settings.Security.PairingCodeTtlSeconds), ct: ct).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _pairCreatedTcs = null;
            }

            var pairing = _pairing ?? throw new InvalidOperationException("信令服务未返回配对信息。");
            PairingCreated?.Invoke(this, pairing);
            return pairing;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task JoinAsync(string code, CancellationToken ct)
    {
        if (!PairingCode.IsWellFormed(code))
        {
            throw new ArgumentException("配对码必须是 6 位数字。", nameof(code));
        }

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state.State is not (ShareSessionState.Idle or ShareSessionState.Failed or ShareSessionState.Disconnected))
            {
                throw new InvalidOperationException("当前状态下不能加入配对。");
            }

            ResetForNewSession();
            _state.Transition(ShareSessionState.Pairing, "加入配对");
            var settings = _settings.Current;
            await EnsureSignalingAsync(settings, ct).ConfigureAwait(false);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _joinedTcs = tcs;
            await _signaling!.SendAsync(SignalMessageType.PairJoin, new PairJoinPayload(code), ct: ct)
                .ConfigureAwait(false);
            Notice?.Invoke(this, "已发送加入请求，等待对方确认…");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.Network.ConnectionTimeoutSeconds));
            try
            {
                await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                RaiseError(new LinkViewError(ErrorCategory.Pairing, "PAIRING_TIMEOUT",
                    "等待对方确认超时。", "join consent timeout", true, RecoveryAction.RePair));
            }
            finally
            {
                _joinedTcs = null;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task AcceptJoinAsync() => RespondToJoinAsync(true);

    public Task RejectJoinAsync() => RespondToJoinAsync(false);

    private async Task RespondToJoinAsync(bool accept)
    {
        var signaling = _signaling;
        var sessionId = _sessionId;
        if (signaling is null || sessionId is null)
        {
            return;
        }

        await signaling.SendAsync(SignalMessageType.PairDecision,
            new PairDecisionPayload(accept, accept ? null : "user rejected"), sessionId).ConfigureAwait(false);
        if (!accept)
        {
            Notice?.Invoke(this, "已拒绝对方的连接请求");
        }
    }

    private async Task EnsureSignalingAsync(AppSettings settings, CancellationToken ct)
    {
        if (_signaling is { IsConnected: true })
        {
            return;
        }

        if (_signaling is not null)
        {
            await _signaling.DisposeAsync().ConfigureAwait(false);
        }

        _sessionCts = new CancellationTokenSource();
        _signalProcessor = Task.Run(() => ProcessSignalsAsync(_sessionCts.Token));
        _signaling = new SignalingClient(_identity, _loggerFactory.CreateLogger<SignalingClient>());
        _signaling.MessageReceived += OnSignalingMessage;
        _signaling.Closed += OnSignalingClosed;
        try
        {
            await _signaling.ConnectAsync(
                settings.Network.SignalingServer,
                settings.General.DeviceName,
                DeviceType.Windows,
                typeof(SessionCoordinator).Assembly.GetName().Version?.ToString(3) ?? "0.9.0",
                ct).ConfigureAwait(false);
        }
        catch (SignalingException ex)
        {
            RaiseError(LinkViewErrors.SignalingConnectFailed(settings.Network.SignalingServer, ex.Message));
            Fail("signaling unavailable");
            throw;
        }
    }

    private void OnSignalingMessage(object? sender, SignalEnvelope envelope)
    {
        _signalQueue.Writer.TryWrite(envelope);
    }

    private async Task ProcessSignalsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in _signalQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await HandleSignalAsync(envelope, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "signal_handling_failed {Type}", envelope.Type);
                    RaiseError(LinkViewErrors.Unknown(ex.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleSignalAsync(SignalEnvelope envelope, CancellationToken ct)
    {
        switch (envelope.Type)
        {
            case SignalMessageType.PairCreated:
            {
                var payload = SignalCodec.GetPayload<PairCreatedPayload>(envelope);
                if (payload is null) return;
                _sessionId = payload.SessionId;
                _signaling!.SessionId = payload.SessionId;
                _pairing = new PairingInfo(payload.SessionId, payload.PairingCode,
                    DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAtUnixMs));
                _pairCreatedTcs?.TrySetResult(true);
                break;
            }
            case SignalMessageType.JoinRequest:
            {
                var payload = SignalCodec.GetPayload<JoinRequestPayload>(envelope);
                if (payload is null) return;
                if (_state.State == ShareSessionState.Pairing)
                {
                    _state.Transition(ShareSessionState.AwaitingConsent, "收到加入请求");
                }

                IncomingJoinRequest?.Invoke(this, payload.Guest);
                if (!_settings.Current.Connection.RequirePairingConfirmation)
                {
                    await RespondToJoinAsync(true).ConfigureAwait(false);
                }
                break;
            }
            case SignalMessageType.PairJoined:
            {
                var payload = SignalCodec.GetPayload<PairJoinedPayload>(envelope);
                if (payload is null) return;
                _sessionId = payload.SessionId;
                _peerDevice = payload.Peer;
                _role = payload.Role;
                _devices.Upsert(payload.Peer);
                if (_state.State is ShareSessionState.Pairing or ShareSessionState.AwaitingConsent)
                {
                    _state.Transition(ShareSessionState.Negotiating, "配对已建立");
                }

                PeerJoined?.Invoke(this, (payload.Peer, payload.Role));
                _joinedTcs?.TrySetResult(true);
                await StartWebRtcAsync(payload, ct).ConfigureAwait(false);
                break;
            }
            case SignalMessageType.SdpOffer:
            {
                var payload = SignalCodec.GetPayload<SdpPayload>(envelope);
                if (payload is null) return;
                if (_peer is null)
                {
                    // Offer 可能比本端的 PairJoined 处理得更早到达；先缓存，等 Peer 初始化后应用。
                    _pendingRemoteOffer = payload.Sdp;
                    return;
                }

                var answer = await _peer.AcceptOfferAsync(payload.Sdp, ct).ConfigureAwait(false);
                await _signaling!.SendAsync(SignalMessageType.SdpAnswer,
                    new SdpPayload("answer", answer), _sessionId, ct).ConfigureAwait(false);
                if (_state.State == ShareSessionState.Negotiating)
                {
                    _state.Transition(ShareSessionState.Connecting, "SDP 已协商");
                }

                break;
            }
            case SignalMessageType.SdpAnswer:
            {
                var payload = SignalCodec.GetPayload<SdpPayload>(envelope);
                if (payload is null || _peer is null) return;
                _peer.AcceptAnswer(payload.Sdp);
                if (_state.State == ShareSessionState.Negotiating)
                {
                    _state.Transition(ShareSessionState.Connecting, "SDP 已协商");
                }

                break;
            }
            case SignalMessageType.IceCandidate:
            {
                var payload = SignalCodec.GetPayload<IceCandidatePayload>(envelope);
                if (payload is null) return;
                if (_peer is null)
                {
                    if (_pendingCandidates.Count < 64)
                    {
                        _pendingCandidates.Add(payload);
                    }

                    return;
                }

                _peer.AddRemoteCandidate(payload);
                break;
            }
            case SignalMessageType.PeerLeft:
            {
                var payload = SignalCodec.GetPayload<PeerLeftPayload>(envelope);
                await HandlePeerGoneAsync(payload?.Reason ?? "对方已断开", "peer left").ConfigureAwait(false);
                break;
            }
            case SignalMessageType.SessionEnd:
            {
                var payload = SignalCodec.GetPayload<SessionEndPayload>(envelope);
                await HandlePeerGoneAsync(payload?.Reason ?? "会话已结束", "peer ended").ConfigureAwait(false);
                break;
            }
            case SignalMessageType.Error:
            {
                var payload = SignalCodec.GetPayload<ErrorPayload>(envelope);
                if (payload is null) return;
                HandleSignalError(payload);
                break;
            }
        }
    }

    private async Task StartWebRtcAsync(PairJoinedPayload payload, CancellationToken ct)
    {
        var settings = _settings.Current;
        var turns = settings.Network.TurnServers.Select(t => t with
        {
            Credential = _secrets.Get(SecretKeyForTurn(t.Uri))
        }).ToArray();

        var peer = new WebRtcSession(_loggerFactory.CreateLogger<WebRtcSession>());
        peer.LocalCandidate += (_, candidate) =>
        {
            _ = SendCandidateAsync(candidate);
        };
        peer.FrameDecoded += (_, frame) =>
        {
            _lastRemoteVideoAt = DateTimeOffset.UtcNow;
            RemoteFrame?.Invoke(this, frame);
        };
        peer.ConnectionStateChanged += (_, state) => _ = OnPeerConnectionStateAsync(state);
        peer.IceStateChanged += (_, state) =>
        {
            if (state == RTCIceConnectionState.failed)
            {
                RaiseError(LinkViewErrors.IceFailed("ice connection failed"));
                _ = FailAsync("ice failed");
            }
        };
        peer.DecodeError += (_, message) => RaiseError(LinkViewErrors.DecoderInitFailed("VP8", message));
        peer.Initialize(settings, turns, ResolveEffectivePreference(settings.Network.Preference));
        _peer = peer;

        if (payload.Role == SessionRole.Host)
        {
            var offer = await peer.CreateOfferAsync(ct).ConfigureAwait(false);
            await _signaling!.SendAsync(SignalMessageType.SdpOffer,
                new SdpPayload("offer", offer), _sessionId, ct).ConfigureAwait(false);
            if (_state.State == ShareSessionState.Negotiating)
            {
                _state.Transition(ShareSessionState.Connecting, "发送 SDP Offer");
            }
        }
        else if (_pendingRemoteOffer is { } pendingOffer)
        {
            _pendingRemoteOffer = null;
            var answer = await peer.AcceptOfferAsync(pendingOffer, ct).ConfigureAwait(false);
            await _signaling!.SendAsync(SignalMessageType.SdpAnswer,
                new SdpPayload("answer", answer), _sessionId, ct).ConfigureAwait(false);
            if (_state.State == ShareSessionState.Negotiating)
            {
                _state.Transition(ShareSessionState.Connecting, "已应答 SDP Offer");
            }
        }

        foreach (var candidate in _pendingCandidates)
        {
            peer.AddRemoteCandidate(candidate);
        }

        _pendingCandidates.Clear();

        _historyRecord = new ConnectionRecord
        {
            PeerName = payload.Peer.DeviceName,
            PeerDeviceId = payload.Peer.DeviceId,
            Direction = "receive",
            StartedAt = DateTimeOffset.UtcNow,
            Result = "connected"
        };
    }

    private async Task SendCandidateAsync(LocalIceCandidate candidate)
    {
        try
        {
            var signaling = _signaling;
            if (signaling is { IsConnected: true } && _sessionId is not null)
            {
                await signaling.SendAsync(SignalMessageType.IceCandidate,
                    new IceCandidatePayload(candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex,
                        candidate.UsernameFragment), _sessionId).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SignalingException or OperationCanceledException)
        {
            _logger.LogDebug("ice_candidate_send_failed {Error}", ex.Message);
        }
    }

    private async Task OnPeerConnectionStateAsync(RTCPeerConnectionState state)
    {
        switch (state)
        {
            case RTCPeerConnectionState.connected:
                _connectedAt ??= DateTimeOffset.UtcNow;
                if (_state.State is ShareSessionState.Connecting or ShareSessionState.Reconnecting)
                {
                    _state.Transition(ShareSessionState.Connected, "WebRTC 已连接");
                    _reconnectAttempts = 0;
                    _reconnectCts?.Cancel();
                }

                if (_sharing && _state.State == ShareSessionState.Connected && _capture.IsCapturing)
                {
                    _state.Transition(ShareSessionState.Sharing, "正在共享");
                }

                Notice?.Invoke(this, "连接已建立");
                break;
            case RTCPeerConnectionState.disconnected:
                if (_state.State is ShareSessionState.Connected or ShareSessionState.Sharing)
                {
                    _state.Transition(ShareSessionState.Reconnecting, "连接暂时中断");
                    StartReconnectWatchdog();
                }

                break;
            case RTCPeerConnectionState.failed:
                RaiseError(LinkViewErrors.IceFailed("peer connection failed"));
                await FailAsync("peer connection failed").ConfigureAwait(false);
                break;
            case RTCPeerConnectionState.closed:
                if (_state.State is not (ShareSessionState.Idle or ShareSessionState.Disconnected or ShareSessionState.Failed))
                {
                    await HandlePeerGoneAsync("连接已关闭", "peer closed").ConfigureAwait(false);
                }

                break;
        }
    }

    private void StartReconnectWatchdog()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        var network = _settings.Current.Network;
        var autoReconnect = _settings.Current.Connection.AutoReconnect;
        var delay = TimeSpan.FromMilliseconds(Math.Clamp(
            network.ReconnectInitialDelayMs * Math.Pow(2, Math.Max(0, _reconnectAttempts)),
            250,
            Math.Max(250, network.ReconnectMaxDelayMs)));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                if (_peer?.IsConnected == true || _state.State != ShareSessionState.Reconnecting)
                {
                    return;
                }

                if (!autoReconnect)
                {
                    RaiseError(LinkViewErrors.IceFailed("auto reconnect disabled"));
                    await FailAsync("auto reconnect disabled").ConfigureAwait(false);
                    return;
                }

                var maxAttempts = Math.Clamp(network.ReconnectMaxAttempts, 0, 20);
                if (_reconnectAttempts >= maxAttempts)
                {
                    RaiseError(LinkViewErrors.IceFailed("reconnect attempts exhausted"));
                    await FailAsync("reconnect exhausted").ConfigureAwait(false);
                    return;
                }

                _reconnectAttempts++;
                Interlocked.Increment(ref _reconnects);
                Notice?.Invoke(this, $"正在尝试恢复连接（第 {_reconnectAttempts} 次）…");
                if (_role == SessionRole.Host && _peer is not null && _signaling is not null)
                {
                    _peer.RestartIce();
                    var offer = await _peer.CreateOfferAsync(token).ConfigureAwait(false);
                    await _signaling.SendAsync(SignalMessageType.SdpOffer,
                        new SdpPayload("offer", offer), _sessionId, token).ConfigureAwait(false);
                    StartReconnectWatchdog();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning("reconnect_failed {Error}", ex.Message);
            }
        }, token);
    }

    /// <summary>
    /// “优先”策略在网络缺少该协议族时自动回退到双栈，避免因为“IPv6 优先”而放弃可用的 IPv4。
    /// </summary>
    private NetworkPreference ResolveEffectivePreference(NetworkPreference preference)
    {
        var usable = _networkInfo.GetAddresses().Where(a => a.IsUsableForConnection).ToArray();
        var hasV4 = usable.Any(a => a.Family == IpFamily.IPv4);
        var hasV6 = usable.Any(a => a.Family == IpFamily.IPv6);
        var effective = preference switch
        {
            NetworkPreference.Ipv4Preferred => hasV4 ? NetworkPreference.Ipv4Only : NetworkPreference.Auto,
            NetworkPreference.Ipv6Preferred => hasV6 ? NetworkPreference.Ipv6Only : NetworkPreference.Auto,
            NetworkPreference.LanOnly => NetworkPreference.Auto,
            _ => preference
        };

        if (effective != preference)
        {
            _logger.LogInformation("network_preference_fallback {Requested} -> {Effective}", preference, effective);
        }

        return effective;
    }

    private async Task HandlePeerGoneAsync(string reason, string stateReason)
    {
        await StopSharingInternalAsync().ConfigureAwait(false);
        await DisposePeerAsync().ConfigureAwait(false);
        EndHistory("disconnected", reason);
        if (_state.State is not (ShareSessionState.Idle or ShareSessionState.Disconnected or ShareSessionState.Failed))
        {
            _state.TryTransition(ShareSessionState.Disconnected, stateReason);
        }

        PeerLeft?.Invoke(this, reason);
        Notice?.Invoke(this, reason);
        await DisconnectSignalingAsync().ConfigureAwait(false);
    }

    private void HandleSignalError(ErrorPayload error)
    {
        var mapped = error.Code switch
        {
            SignalErrorCode.PairingInvalid => LinkViewErrors.PairingInvalid(error.Message),
            SignalErrorCode.PairingExpired => LinkViewErrors.PairingInvalid(error.Message),
            SignalErrorCode.PairingLocked => LinkViewErrors.PairingLocked(error.Message),
            SignalErrorCode.PeerUnavailable => LinkViewErrors.PeerOffline(error.Message),
            SignalErrorCode.Unauthorized or SignalErrorCode.AuthFailed =>
                LinkViewErrors.SignalingAuthFailed(error.Message),
            SignalErrorCode.RateLimited => new LinkViewError(ErrorCategory.Signaling, "SIGNALING_RATE_LIMITED",
                "操作过于频繁，请稍后再试。", error.Message, true, RecoveryAction.Retry),
            _ => LinkViewErrors.Unknown(error.Message)
        };

        _joinedTcs?.TrySetException(new InvalidOperationException(mapped.UserMessage));
        RaiseError(mapped);
        if (_state.State is ShareSessionState.Pairing or ShareSessionState.AwaitingConsent or
            ShareSessionState.Negotiating)
        {
            _state.TryTransition(ShareSessionState.Failed, "signaling error");
        }
    }

    private async Task FailAsync(string reason)
    {
        await StopSharingInternalAsync().ConfigureAwait(false);
        await DisposePeerAsync().ConfigureAwait(false);
        EndHistory("failed", reason);
        _state.TryTransition(ShareSessionState.Failed, reason);
        await DisconnectSignalingAsync().ConfigureAwait(false);
    }

    private void Fail(string reason)
    {
        _state.TryTransition(ShareSessionState.Failed, reason);
    }

    public async Task StartSharingAsync(CaptureSource source, CancellationToken ct)
    {
        if (_state.State is not (ShareSessionState.Connected or ShareSessionState.Sharing))
        {
            throw new InvalidOperationException("需要先建立连接才能开始共享。");
        }

        if (_sharing)
        {
            return;
        }

        _capture.FrameAvailable -= OnLocalFrame;
        _capture.FrameAvailable += OnLocalFrame;
        _capture.SourceLost += OnCaptureLost;
        _capture.ErrorOccurred += OnCaptureError;
        try
        {
            await _capture.StartAsync(source, _settings.Current.Video, ct).ConfigureAwait(false);
            _captureSource = source;
            _sharing = true;
            _paused = false;
            _lastLocalFrameAt = null;
            _lastKeyFrameAt = null;
            _peer?.ForceKeyFrame();
            if (_state.State == ShareSessionState.Connected)
            {
                _state.Transition(ShareSessionState.Sharing, "开始共享屏幕");
            }

            if (_historyRecord is not null)
            {
                _historyRecord.Direction = "share";
            }
        }
        catch (CaptureException ex)
        {
            RaiseError(ex.Code switch
            {
                "CAPTURE_ITEM_FAILED" => LinkViewErrors.CaptureSourceLost(ex.Detail ?? ex.Message),
                _ => LinkViewErrors.CaptureStartFailed(ex.Detail ?? ex.Message)
            });
            throw;
        }
    }

    public void PauseSharing()
    {
        if (!_sharing || _paused)
        {
            return;
        }

        _paused = true;
        _capture.IsPaused = true;
        if (_state.State == ShareSessionState.Sharing)
        {
            _state.Transition(ShareSessionState.Connected, "共享已暂停");
        }
    }

    public void ResumeSharing()
    {
        if (!_sharing || !_paused)
        {
            return;
        }

        _paused = false;
        _capture.IsPaused = false;
        _peer?.ForceKeyFrame();
        if (_state.State == ShareSessionState.Connected)
        {
            _state.Transition(ShareSessionState.Sharing, "恢复共享");
        }
    }

    public async Task StopSharingAsync()
    {
        await StopSharingInternalAsync().ConfigureAwait(false);
        if (_state.State == ShareSessionState.Sharing)
        {
            _state.Transition(ShareSessionState.Connected, "停止共享");
        }
    }

    private async Task StopSharingInternalAsync()
    {
        _capture.FrameAvailable -= OnLocalFrame;
        _capture.SourceLost -= OnCaptureLost;
        _capture.ErrorOccurred -= OnCaptureError;
        if (_capture.IsCapturing)
        {
            await _capture.StopAsync().ConfigureAwait(false);
        }

        _sharing = false;
        _paused = false;
        _captureSource = null;
    }

    private void OnLocalFrame(object? sender, CapturedFrame frame)
    {
        if (!_sharing || _paused)
        {
            return;
        }

        var video = _settings.Current.Video;
        var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, video.TargetFps));
        var now = DateTimeOffset.UtcNow;
        if (_lastLocalFrameAt is { } last && now - last < interval)
        {
            return;
        }

        _lastLocalFrameAt = now;
        PreviewFrame?.Invoke(this, frame);
        try
        {
            var peer = _peer;
            if (peer?.IsConnected != true)
            {
                return;
            }

            var keyframeInterval = TimeSpan.FromSeconds(Math.Clamp(video.KeyframeIntervalSeconds, 1, 10));
            if (_lastKeyFrameAt is null || now - _lastKeyFrameAt >= keyframeInterval)
            {
                peer.ForceKeyFrame();
                _lastKeyFrameAt = now;
            }

            peer.EncodeAndSend(frame.Bgra, frame.Width, frame.Height, video.TargetFps);
        }
        catch (Exception ex)
        {
            RaiseError(LinkViewErrors.EncoderFailed(ex.Message));
            _ = StopSharingAsync();
        }
    }

    private void OnCaptureLost(object? sender, string message)
    {
        _ = Task.Run(async () =>
        {
            RaiseError(LinkViewErrors.CaptureSourceLost(message));
            await StopSharingAsync().ConfigureAwait(false);
        });
    }

    private void OnCaptureError(object? sender, string message)
    {
        _logger.LogWarning("capture_error {Error}", message);
    }

    public async Task DisconnectAsync(string reason = "用户断开连接")
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state.State is ShareSessionState.Idle or ShareSessionState.Disconnected)
            {
                return;
            }

            var wasFailed = _state.State == ShareSessionState.Failed;
            if (!wasFailed)
            {
                _state.TryTransition(ShareSessionState.Stopping, "用户停止");
            }
            if (_signaling is { IsConnected: true } && _sessionId is not null)
            {
                try
                {
                    await _signaling.SendAsync(SignalMessageType.SessionEnd,
                        new SessionEndPayload(reason), _sessionId).ConfigureAwait(false);
                }
                catch (SignalingException)
                {
                }
            }

            await StopSharingInternalAsync().ConfigureAwait(false);
            await DisposePeerAsync().ConfigureAwait(false);
            EndHistory("completed", reason);
            await DisconnectSignalingAsync().ConfigureAwait(false);
            if (wasFailed)
            {
                _state.TryTransition(ShareSessionState.Idle, "reset after failure");
            }
            else
            {
                _state.TryTransition(ShareSessionState.Disconnected, reason);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task DisconnectSignalingAsync()
    {
        var signaling = _signaling;
        _signaling = null;
        if (signaling is not null)
        {
            signaling.MessageReceived -= OnSignalingMessage;
            signaling.Closed -= OnSignalingClosed;
            await signaling.DisposeAsync().ConfigureAwait(false);
        }

        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
    }

    private void OnSignalingClosed(object? sender, string? reason)
    {
        if (_state.State is ShareSessionState.Connected or ShareSessionState.Sharing or
            ShareSessionState.Reconnecting or ShareSessionState.Connecting)
        {
            RaiseError(LinkViewErrors.PeerOffline(reason ?? "信令连接已断开"));
            _ = HandlePeerGoneAsync("信令连接已断开", "signaling closed");
        }
    }

    private async Task DisposePeerAsync()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        var peer = _peer;
        _peer = null;
        if (peer is not null)
        {
            await peer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EndHistory(string result, string? detail)
    {
        if (_historyRecord is null)
        {
            return;
        }

        var stats = _peer?.GetStats();
        _historyRecord.EndedAt = DateTimeOffset.UtcNow;
        _historyRecord.Result = result;
        _historyRecord.Detail = detail;
        _historyRecord.Codec = stats?.Codec;
        _historyRecord.SentBytes = stats?.BytesSent;
        _historyRecord.ReceivedBytes = stats?.BytesReceived;
        _history.Add(_historyRecord);
        _historyRecord = null;
    }

    private void ResetForNewSession()
    {
        _peerDevice = null;
        _role = null;
        _sessionId = null;
        _pairing = null;
        _connectedAt = null;
        _lastRemoteVideoAt = null;
        _lastLocalFrameAt = null;
        _lastKeyFrameAt = null;
        _reconnects = 0;
        _freezes = 0;
        _reconnectAttempts = 0;
        _historyRecord = null;
        _pendingRemoteOffer = null;
        _pendingCandidates.Clear();
        lock (_statsSync)
        {
            _lastRateSample = DateTime.UtcNow;
            _lastSentFrames = _lastSentBytes = _lastRecvFrames = _lastRecvBytes = 0;
            _sentFps = _sentKbps = _recvFps = _recvKbps = 0;
        }
    }

    public LiveStats GetLiveStats()
    {
        var peerStats = _peer?.GetStats();
        var now = DateTime.UtcNow;
        lock (_statsSync)
        {
            var seconds = Math.Max(0.2, (now - _lastRateSample).TotalSeconds);
            if (peerStats is not null)
            {
                var sentFrames = peerStats.FramesSent;
                var sentBytes = peerStats.BytesSent;
                var recvFrames = peerStats.FramesReceived;
                var recvBytes = peerStats.BytesReceived;
                _sentFps = (sentFrames - _lastSentFrames) / seconds;
                _sentKbps = (sentBytes - _lastSentBytes) * 8 / 1000.0 / seconds;
                _recvFps = (recvFrames - _lastRecvFrames) / seconds;
                _recvKbps = (recvBytes - _lastRecvBytes) * 8 / 1000.0 / seconds;
                _lastSentFrames = sentFrames;
                _lastSentBytes = sentBytes;
                _lastRecvFrames = recvFrames;
                _lastRecvBytes = recvBytes;
            }

            _lastRateSample = now;
        }

        var settings = _settings.Current;
        var path = settings.Network.IceTransport == IceTransportMode.RelayOnly
            ? "TURN 中继（强制）"
            : settings.Network.TurnServers.Count > 0
                ? "自动（直连优先，失败可中继）"
                : "自动（仅直连候选）";

        var duration = _connectedAt is { } started
            ? DateTimeOffset.UtcNow - started
            : TimeSpan.Zero;

        return new LiveStats(
            _state.State,
            _peerDevice?.DeviceName,
            _role,
            duration,
            (int)Math.Round(_sentFps),
            _sentKbps,
            (int)Math.Round(_recvFps),
            _recvKbps,
            peerStats?.AverageEncodeMs ?? 0,
            peerStats?.AverageDecodeMs ?? 0,
            peerStats?.SendWidth ?? 0,
            peerStats?.SendHeight ?? 0,
            peerStats?.LastDecodedWidth ?? 0,
            peerStats?.LastDecodedHeight ?? 0,
            peerStats?.Codec ?? "VP8",
            peerStats?.ConnectionState ?? _state.State.ToString(),
            peerStats?.IceState ?? "unknown",
            path,
            Interlocked.Read(ref _reconnects),
            Interlocked.Read(ref _freezes),
            _sharing && !_paused,
            _lastRemoteVideoAt,
            _captureSource?.DisplayName);
    }

    private void RaiseError(LinkViewError error)
    {
        _logger.Log(LogLevel.Warning, "session_error {Category}/{Code} {Message}", error.Category, error.Code,
            error.UserMessage);
        ErrorOccurred?.Invoke(this, error);
    }

    public static string SecretKeyForTurn(string uri) => "turn:" + uri;

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync("应用退出").ConfigureAwait(false);
        _capture.Dispose();
        _signalQueue.Writer.TryComplete();
        _operationLock.Dispose();
    }
}
