namespace LinkView.Core.Errors;

public enum ErrorCategory
{
    Configuration,
    Discovery,
    Signaling,
    Pairing,
    Permissions,
    Network,
    NatTraversal,
    WebRtcNegotiation,
    IceConnectivity,
    MediaCapture,
    Encoding,
    Decoding,
    Rendering,
    Security,
    Session,
    Unknown
}

public enum RecoveryAction
{
    None,
    Retry,
    RePair,
    Reauthorize,
    ChangeSettings,
    CheckNetwork,
    EnableRelay,
    RestartCapture,
    RestartApp
}

/// <summary>
/// 统一的错误模型。每个错误都必须能回答：发生在哪个环节、用户能做什么、
/// 是否值得自动重试、日志里用什么代码检索。
/// </summary>
public sealed record LinkViewError(
    ErrorCategory Category,
    string Code,
    string UserMessage,
    string TechnicalDetail,
    bool IsRetryable,
    RecoveryAction SuggestedAction)
{
    public override string ToString() => $"[{Category}/{Code}] {UserMessage}";
}
