using System.ComponentModel.DataAnnotations;

namespace LinkView.Signaling;

/// <summary>
/// 信令服务运行参数。信令服务只转发连接协商所需的消息，不接触媒体内容。
/// </summary>
public sealed class SignalingOptions
{
    public const string SectionName = "Signaling";

    [Range(1, 100_000)]
    public int MaxConnections { get; set; } = 2_000;

    [Range(1, 10_000)]
    public int MaxSessions { get; set; } = 1_000;

    [Range(1, 100)]
    public int MaxSessionsPerDevice { get; set; } = 2;

    [Range(1, 1_000)]
    public int MaxMessagesPerSecondPerConnection { get; set; } = Protocol.ProtocolConstants.MaxMessagesPerSecond;

    [Range(30, 3_600)]
    public int HeartbeatTimeoutSeconds { get; set; } = 180;

    [Range(10, 600)]
    public int CleanupIntervalSeconds { get; set; } = 30;

    /// <summary>会话建立后，等待双方完成 WebRTC 协商的最长时间。</summary>
    [Range(60, 3_600)]
    public int NegotiationTimeoutSeconds { get; set; } = 120;
}
