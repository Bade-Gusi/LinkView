using System.Text.Json;

namespace LinkView.Protocol;

public enum SignalMessageType
{
    Hello,
    AuthChallenge,
    AuthProof,
    AuthOk,
    PairCreate,
    PairCreated,
    PairJoin,
    JoinRequest,
    PairDecision,
    PairJoined,
    PeerLeft,
    SdpOffer,
    SdpAnswer,
    IceCandidate,
    SessionEnd,
    Ping,
    Pong,
    Error
}

public enum DeviceType
{
    Unknown,
    Windows,
    Android,
    Web
}

public enum SessionRole
{
    Host,
    Guest
}

public enum SignalErrorCode
{
    InvalidMessage,
    UnsupportedVersion,
    Unauthorized,
    AuthFailed,
    AuthExpired,
    RateLimited,
    PayloadTooLarge,
    PairingInvalid,
    PairingExpired,
    PairingLocked,
    SessionNotFound,
    SessionFull,
    NotInSession,
    PeerUnavailable,
    Internal
}

/// <summary>信令信封。Payload 为强类型负载的 JSON 对象，必须由服务端验证其类型和大小。</summary>
public sealed record SignalEnvelope
{
    public int V { get; init; } = ProtocolConstants.Version;
    public required string Id { get; init; }
    public required SignalMessageType Type { get; init; }
    public string? SessionId { get; init; }
    public long Ts { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public JsonElement Payload { get; init; }
}

public sealed record DeviceSummary(
    string DeviceId,
    string DeviceName,
    DeviceType DeviceType,
    string AppVersion,
    string? ObservedAddress = null);

public sealed record HelloPayload(
    string DeviceId,
    string DeviceName,
    DeviceType DeviceType,
    string PublicKey,
    string AppVersion,
    int ProtocolVersion);

public sealed record AuthChallengePayload(string Nonce, long ExpiresAtUnixMs);

public sealed record AuthProofPayload(string Signature, long ClientTimestampUnixMs);

public sealed record AuthOkPayload(DeviceSummary Self, int HeartbeatSeconds);

public sealed record PairCreatePayload(int TtlSeconds);

public sealed record PairCreatedPayload(string SessionId, string PairingCode, long ExpiresAtUnixMs);

public sealed record PairJoinPayload(string PairingCode);

public sealed record JoinRequestPayload(string SessionId, DeviceSummary Guest);

public sealed record PairDecisionPayload(bool Accept, string? Reason);

public sealed record PairJoinedPayload(string SessionId, DeviceSummary Peer, SessionRole Role);

public sealed record PeerLeftPayload(string Reason);

public sealed record SdpPayload(string SdpType, string Sdp);

public sealed record IceCandidatePayload(
    string Candidate,
    string? SdpMid,
    int? SdpMLineIndex,
    string? UsernameFragment);

public sealed record SessionEndPayload(string Reason);

public sealed record PingPayload(long ClientTimeMs);

public sealed record PongPayload(long ClientTimeMs, long ServerTimeMs);

public sealed record ErrorPayload(SignalErrorCode Code, string Message, int? RetryAfterMs = null);
