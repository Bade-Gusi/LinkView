namespace LinkView.Signaling;

public enum PairingPhase
{
    WaitingForGuest,
    AwaitingHostConsent,
    Joined,
    Closed
}

/// <summary>
/// 一次临时配对会话。配对码只保存在内存中并有有效期；会话关闭后立即清除。
/// </summary>
public sealed class PairingSession
{
    public required string Id { get; init; }
    public required ClientConnection Host { get; init; }
    public ClientConnection? Guest { get; set; }
    public required string Code { get; init; }
    public required string CodeSalt { get; init; }
    public required string CodeHash { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset PairingExpiresAtUtc { get; set; }
    public DateTimeOffset NegotiationDeadlineUtc { get; set; }
    public PairingPhase Phase { get; set; } = PairingPhase.WaitingForGuest;
    public object SyncRoot { get; } = new();

    public bool IsParticipant(ClientConnection connection) =>
        ReferenceEquals(connection, Host) || ReferenceEquals(connection, Guest);

    public ClientConnection? PeerOf(ClientConnection connection) =>
        ReferenceEquals(connection, Host) ? Guest : ReferenceEquals(connection, Guest) ? Host : null;
}
