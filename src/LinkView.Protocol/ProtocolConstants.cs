namespace LinkView.Protocol;

public static class ProtocolConstants
{
    /// <summary>信令协议版本。任何破坏兼容性的字段变更都必须递增此版本。</summary>
    public const int Version = 1;

    /// <summary>单条信令消息允许的最大字节数（SDP 是其中最大的消息）。</summary>
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>客户端允许发送的最大信令速率（每秒消息数）。</summary>
    public const int MaxMessagesPerSecond = 40;

    /// <summary>认证挑战有效时间。</summary>
    public static readonly TimeSpan AuthChallengeLifetime = TimeSpan.FromSeconds(60);

    /// <summary>客户端时间戳允许的偏差。</summary>
    public static readonly TimeSpan AuthClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>默认配对码有效期。</summary>
    public static readonly TimeSpan DefaultPairingLifetime = TimeSpan.FromMinutes(5);

    /// <summary>同一连接允许的最大错误配对尝试次数，超过后进入冷却期。</summary>
    public const int MaxPairingAttempts = 5;

    /// <summary>错误配对达到上限后的冷却时间。</summary>
    public static readonly TimeSpan PairingLockoutDuration = TimeSpan.FromSeconds(60);

    /// <summary>同一 IP 在统计窗口内允许的错误配对次数。</summary>
    public const int MaxPairingFailuresPerIpWindow = 30;

    public static readonly TimeSpan PairingFailureWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PairingIpBlockDuration = TimeSpan.FromMinutes(5);

    /// <summary>信令会话的最长存活时间，防止僵尸会话。</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    /// <summary>认证签名的规范化前缀。</summary>
    public const string AuthCanonicalPrefix = "LinkView-Auth-v1";
}
