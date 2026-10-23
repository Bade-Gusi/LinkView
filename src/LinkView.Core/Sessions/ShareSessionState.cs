namespace LinkView.Core.Sessions;

/// <summary>
/// 屏幕共享会话状态。所有界面展示和资源生命周期都以这个状态机为准，
/// 不使用互相独立的 IsConnecting / IsSharing 布尔标志。
/// </summary>
public enum ShareSessionState
{
    Idle,
    Discovering,
    DeviceFound,
    Pairing,
    AwaitingConsent,
    Negotiating,
    Connecting,
    Connected,
    Sharing,
    Reconnecting,
    Stopping,
    Disconnected,
    Failed
}

public sealed record SessionStateChange(
    ShareSessionState From,
    ShareSessionState To,
    string Reason,
    DateTimeOffset At);

/// <summary>
/// 受控状态转换表。非法转换会被拒绝并记录，避免异步回调把会话带入矛盾状态。
/// </summary>
public sealed class ShareSessionStateMachine
{
    private static readonly Dictionary<ShareSessionState, ShareSessionState[]> Allowed = new()
    {
        [ShareSessionState.Idle] = [ShareSessionState.Discovering, ShareSessionState.Pairing, ShareSessionState.Failed],
        [ShareSessionState.Discovering] = [ShareSessionState.Idle, ShareSessionState.DeviceFound, ShareSessionState.Pairing, ShareSessionState.Failed],
        [ShareSessionState.DeviceFound] = [ShareSessionState.Discovering, ShareSessionState.Pairing, ShareSessionState.Idle, ShareSessionState.Failed],
        [ShareSessionState.Pairing] = [ShareSessionState.AwaitingConsent, ShareSessionState.Negotiating, ShareSessionState.Idle, ShareSessionState.Failed],
        [ShareSessionState.AwaitingConsent] = [ShareSessionState.Negotiating, ShareSessionState.Idle, ShareSessionState.Failed],
        [ShareSessionState.Negotiating] = [ShareSessionState.Connecting, ShareSessionState.Idle, ShareSessionState.Failed],
        [ShareSessionState.Connecting] = [ShareSessionState.Connected, ShareSessionState.Reconnecting, ShareSessionState.Stopping, ShareSessionState.Failed],
        [ShareSessionState.Connected] = [ShareSessionState.Sharing, ShareSessionState.Reconnecting, ShareSessionState.Stopping, ShareSessionState.Disconnected, ShareSessionState.Failed],
        [ShareSessionState.Sharing] = [ShareSessionState.Connected, ShareSessionState.Reconnecting, ShareSessionState.Stopping, ShareSessionState.Disconnected, ShareSessionState.Failed],
        [ShareSessionState.Reconnecting] = [ShareSessionState.Negotiating, ShareSessionState.Connecting, ShareSessionState.Stopping, ShareSessionState.Disconnected, ShareSessionState.Failed],
        [ShareSessionState.Stopping] = [ShareSessionState.Disconnected, ShareSessionState.Idle, ShareSessionState.Failed],
        [ShareSessionState.Disconnected] = [ShareSessionState.Idle, ShareSessionState.Discovering, ShareSessionState.Pairing],
        [ShareSessionState.Failed] = [ShareSessionState.Idle, ShareSessionState.Pairing, ShareSessionState.Discovering]
    };

    public ShareSessionState State { get; private set; } = ShareSessionState.Idle;

    public ShareSessionState PreviousState { get; private set; } = ShareSessionState.Idle;

    public string LastReason { get; private set; } = string.Empty;

    public event EventHandler<SessionStateChange>? StateChanged;

    public static bool IsTransitionAllowed(ShareSessionState from, ShareSessionState to) =>
        from == to || Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public bool TryTransition(ShareSessionState to, string reason)
    {
        if (!IsTransitionAllowed(State, to))
        {
            return false;
        }

        var change = new SessionStateChange(State, to, reason, DateTimeOffset.UtcNow);
        PreviousState = State;
        State = to;
        LastReason = reason;
        StateChanged?.Invoke(this, change);
        return true;
    }

    public void Transition(ShareSessionState to, string reason)
    {
        if (!TryTransition(to, reason))
        {
            throw new InvalidOperationException($"非法的会话状态转换：{State} -> {to}（{reason}）");
        }
    }

    public static bool RequiresMediaRelease(ShareSessionState state) =>
        state is ShareSessionState.Sharing or ShareSessionState.Connected or ShareSessionState.Reconnecting;

    public static bool AllowsUserStop(ShareSessionState state) =>
        state is not (ShareSessionState.Idle or ShareSessionState.Stopping or ShareSessionState.Disconnected);

    public static bool CanRetry(ShareSessionState state) =>
        state is ShareSessionState.Failed or ShareSessionState.Disconnected;

    public static bool RequiresReauthorization(ShareSessionState state) =>
        state is ShareSessionState.Failed;

    public void Reset(string reason) => TryTransition(ShareSessionState.Idle, reason);
}
