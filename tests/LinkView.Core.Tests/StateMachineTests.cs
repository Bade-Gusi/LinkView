using LinkView.Core.Sessions;

namespace LinkView.Core.Tests;

public class StateMachineTests
{
    [Fact]
    public void HappyPath_TransitionsExactlyOncePerStage()
    {
        var sm = new ShareSessionStateMachine();
        sm.Transition(ShareSessionState.Pairing, "user starts pairing");
        sm.Transition(ShareSessionState.AwaitingConsent, "join request sent");
        sm.Transition(ShareSessionState.Negotiating, "consent granted");
        sm.Transition(ShareSessionState.Connecting, "sdp exchanged");
        sm.Transition(ShareSessionState.Connected, "ice connected");
        sm.Transition(ShareSessionState.Sharing, "first frame");

        Assert.Equal(ShareSessionState.Sharing, sm.State);
        Assert.Equal(ShareSessionState.Connected, sm.PreviousState);
    }

    [Fact]
    public void IllegalTransition_IsRejectedWithoutChangingState()
    {
        var sm = new ShareSessionStateMachine();
        Assert.False(sm.TryTransition(ShareSessionState.Connected, "skip everything"));
        Assert.Equal(ShareSessionState.Idle, sm.State);
    }

    [Fact]
    public void StateChanged_RaisesWithCorrectPayload()
    {
        var sm = new ShareSessionStateMachine();
        SessionStateChange? captured = null;
        sm.StateChanged += (_, e) => captured = e;

        sm.Transition(ShareSessionState.Discovering, "scan");

        Assert.NotNull(captured);
        Assert.Equal(ShareSessionState.Idle, captured!.From);
        Assert.Equal(ShareSessionState.Discovering, captured.To);
    }

    [Theory]
    [InlineData(ShareSessionState.Sharing)]
    [InlineData(ShareSessionState.Connected)]
    [InlineData(ShareSessionState.Reconnecting)]
    public void MediaStates_RequireRelease(ShareSessionState state)
    {
        Assert.True(ShareSessionStateMachine.RequiresMediaRelease(state));
    }

    [Fact]
    public void StopAllowed_DuringSharing_ButNotWhileStopping()
    {
        Assert.True(ShareSessionStateMachine.AllowsUserStop(ShareSessionState.Sharing));
        Assert.False(ShareSessionStateMachine.AllowsUserStop(ShareSessionState.Stopping));
    }

    [Fact]
    public void Retry_OnlyFromFailedOrDisconnected()
    {
        Assert.True(ShareSessionStateMachine.CanRetry(ShareSessionState.Failed));
        Assert.True(ShareSessionStateMachine.CanRetry(ShareSessionState.Disconnected));
        Assert.False(ShareSessionStateMachine.CanRetry(ShareSessionState.Sharing));
    }

    [Fact]
    public void Failed_CanReturnToPairing_ForNewAttempt()
    {
        var sm = new ShareSessionStateMachine();
        sm.Transition(ShareSessionState.Pairing, "start");
        sm.Transition(ShareSessionState.Failed, "ice failed");
        sm.Transition(ShareSessionState.Pairing, "retry with new code");
        Assert.Equal(ShareSessionState.Pairing, sm.State);
    }

    [Fact]
    public void Disconnected_CanRestartDiscovery()
    {
        var sm = new ShareSessionStateMachine();
        sm.Transition(ShareSessionState.Pairing, "p");
        sm.Transition(ShareSessionState.Negotiating, "n");
        sm.Transition(ShareSessionState.Connecting, "c");
        sm.Transition(ShareSessionState.Connected, "ok");
        sm.Transition(ShareSessionState.Stopping, "stop");
        sm.Transition(ShareSessionState.Disconnected, "closed");
        sm.Transition(ShareSessionState.Discovering, "scan again");
        Assert.Equal(ShareSessionState.Discovering, sm.State);
    }
}
