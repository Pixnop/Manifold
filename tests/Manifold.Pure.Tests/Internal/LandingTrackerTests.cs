using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class LandingTrackerTests
{
    private static readonly PendingLanding First = new(10, 70, 10, 1f);
    private static readonly PendingLanding Second = new(20, 70, 20, null);

    [Fact]
    public void Begin_Should_Give_Every_Teleport_Its_Own_Id()
    {
        var tracker = new LandingTracker();

        Assert.NotEqual(tracker.Begin("alice", First), tracker.Begin("alice", Second));
    }

    [Fact]
    public void GetPending_Should_Return_The_Latest_Landing_Until_It_Is_Applied()
    {
        var tracker = new LandingTracker();
        Assert.Null(tracker.GetPending("alice"));

        tracker.Begin("alice", First);
        long second = tracker.Begin("alice", Second);
        Assert.Equal(Second, tracker.GetPending("alice"));

        tracker.Complete("alice", second, 20, 70, 20);
        Assert.Null(tracker.GetPending("alice"));
    }

    [Fact]
    public void Complete_Should_Apply_The_Yaw_Of_The_Latest_Teleport()
    {
        var tracker = new LandingTracker();
        long id = tracker.Begin("alice", First);

        var outcome = tracker.Complete("alice", id, 10, 70, 10);

        Assert.Equal(1f, outcome.ApplyYaw);
        Assert.Null(outcome.Reissue);
    }

    [Fact]
    public void Complete_Should_Do_Nothing_When_A_Superseded_Teleport_Lands_While_The_Latest_Is_Still_Waiting()
    {
        var tracker = new LandingTracker();
        long first = tracker.Begin("alice", First);
        tracker.Begin("alice", Second);

        var outcome = tracker.Complete("alice", first, 10, 70, 10);

        Assert.Equal(default, outcome);
        Assert.Equal(Second, tracker.GetPending("alice")); // the entry of the latest was not touched
    }

    [Fact]
    public void Complete_Should_Reissue_The_Latest_Landing_When_A_Superseded_Teleport_Displaces_The_Player_After_It_Was_Applied()
    {
        var tracker = new LandingTracker();
        long first = tracker.Begin("alice", First);
        long second = tracker.Begin("alice", Second);
        tracker.Complete("alice", second, 20, 70, 20);

        var outcome = tracker.Complete("alice", first, 10, 70, 10);

        Assert.Null(outcome.ApplyYaw);
        Assert.Equal(Second, outcome.Reissue);
    }

    [Fact]
    public void Complete_Should_Not_Reissue_When_The_Superseded_Teleport_Put_The_Player_On_The_Latest_Coordinates_Anyway()
    {
        var tracker = new LandingTracker();
        long first = tracker.Begin("alice", First);
        long second = tracker.Begin("alice", First with { Yaw = 3f });
        tracker.Complete("alice", second, 10, 70, 10);

        Assert.Equal(default, tracker.Complete("alice", first, 10, 70, 10));
    }

    [Fact]
    public void Complete_Should_Ignore_A_Player_That_Was_Forgotten()
    {
        var tracker = new LandingTracker();
        long id = tracker.Begin("alice", First);

        tracker.Forget("alice");

        Assert.Equal(default, tracker.Complete("alice", id, 10, 70, 10));
        Assert.Null(tracker.GetPending("alice"));
    }

    [Fact]
    public void Players_Should_Be_Tracked_Independently()
    {
        var tracker = new LandingTracker();
        tracker.Begin("alice", First);
        long bob = tracker.Begin("bob", Second);

        tracker.Complete("bob", bob, 20, 70, 20);

        Assert.Equal(First, tracker.GetPending("alice"));
        Assert.Null(tracker.GetPending("bob"));
    }

    [Fact]
    public void A_Reissued_Landing_Should_Not_Be_Superseded_By_The_Teleports_It_Replaces()
    {
        // Latest applied, a stale one displaces the player and is reissued; the reissue is then the
        // latest, applies its yaw, and no further reissue is requested (no ping-pong).
        var tracker = new LandingTracker();
        long first = tracker.Begin("alice", First);
        long second = tracker.Begin("alice", Second);
        tracker.Complete("alice", second, 20, 70, 20);
        var undo = tracker.Complete("alice", first, 10, 70, 10).Reissue!.Value;
        long reissued = tracker.Begin("alice", undo);

        var outcome = tracker.Complete("alice", reissued, 20, 70, 20);

        Assert.Null(outcome.Reissue);
        Assert.Equal(Second.Yaw, outcome.ApplyYaw);
    }
}
