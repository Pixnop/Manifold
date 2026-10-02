using System;
using System.Collections.Generic;
using Manifold.Internal;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// <see cref="RespawnWatcher"/>: a respawn request is answered only for a player seen dying, only
/// once the engine has revived them, and never for a player another player revived in place.
/// </summary>
public sealed class RespawnWatcherTests
{
    [Fact]
    public void OnRespawnRequested_Should_Ignore_A_Player_Who_Never_Died()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
        Assert.Empty(fx.Scheduled);
    }

    [Fact]
    public void OnRespawnRequested_Should_Move_A_Dead_Player_At_Once_When_The_Engine_Already_Revived_Them()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        Fixture.Revive(player); // the engine's respawn teleport applied and revived them within the request

        fx.Watcher.OnRespawnRequested(player);

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void OnRespawnRequested_Should_Wait_For_The_Revive_Without_Polling_When_The_Engine_Teleport_Is_Pending()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Scheduled); // nothing to poll, however long the engine takes
        Assert.Empty(fx.Respawned);

        Fixture.Revive(player);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
        Assert.Equal([RespawnWatcher.DeferMs], fx.Delays);
    }

    [Fact]
    public void OnRespawnRequested_Should_Move_The_Player_Only_Once_When_The_Request_Is_Repeated_While_Waiting()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);

        fx.Watcher.OnRespawnRequested(player);
        fx.Watcher.OnRespawnRequested(player);
        Fixture.Revive(player);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void OnRespawnRequested_Should_Ignore_A_Request_After_The_Player_Was_Moved_Until_They_Die_Again()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        Fixture.Revive(player);
        fx.Watcher.OnRespawnRequested(player);
        fx.RunAllScheduled();

        fx.Watcher.OnRespawnRequested(player); // a request from a living player: ignored
        Assert.Single(fx.Respawned);

        Fixture.Die(player);
        Fixture.Revive(player);
        fx.Watcher.OnRespawnRequested(player);
        Assert.Equal(2, fx.Respawned.Count);
    }

    [Fact]
    public void OnRespawnRequested_Should_Ignore_A_Player_Who_Was_Revived_In_Place()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);

        Fixture.Revive(player); // another player revived them where they fell: no respawn request
        fx.RunAllScheduled(); // the end of that tick
        fx.Watcher.OnRespawnRequested(player); // a request from a player who is alive

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void OnRespawnRequested_Should_Ignore_A_Player_Revived_In_Place_Who_Then_Dies_Again_And_Is_Revived_Again()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        Fixture.Revive(player);
        fx.RunAllScheduled();
        Fixture.Die(player);
        Fixture.Revive(player);
        fx.RunAllScheduled();

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void OnRespawnRequested_Should_Handle_A_Player_Who_Joined_Dead()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice");
        Fixture.Die(player); // they died before they logged out
        fx.Watcher.OnNowPlaying(player);

        fx.Watcher.OnRespawnRequested(player);
        Fixture.Revive(player);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void OnNowPlaying_Should_Not_Follow_A_Player_Without_An_Entity()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice");
        player.Entity.Returns((EntityPlayer?)null);

        fx.Watcher.OnNowPlaying(player);
        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void OnNowPlaying_Should_Follow_A_Player_Once_When_It_Is_Raised_Twice_For_The_Same_Connection()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        fx.Watcher.OnNowPlaying(player);
        Fixture.Die(player);
        Fixture.Revive(player);

        fx.Watcher.OnRespawnRequested(player);

        Assert.Single(fx.Respawned);
    }

    [Fact]
    public void OnDisconnect_Should_Stop_The_Wait_For_A_Player_Who_Left()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        fx.Watcher.OnRespawnRequested(player);

        fx.Watcher.OnDisconnect(player);
        Fixture.Revive(player);
        fx.RunAllScheduled();

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void OnDisconnect_Should_Forget_That_The_Player_Was_Dead()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        fx.Watcher.OnDisconnect(player);

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void OnRespawnRequested_Should_Ignore_The_Old_Entity_When_The_Player_Came_Back_As_A_New_Connection()
    {
        var fx = new Fixture();
        var first = fx.Join("alice");
        Fixture.Die(first);
        fx.Watcher.OnRespawnRequested(first);
        fx.Watcher.OnDisconnect(first);

        var second = fx.NewPlayer("alice");
        Fixture.Die(second);
        fx.Watcher.OnNowPlaying(second);
        fx.Watcher.OnRespawnRequested(second);
        Fixture.Revive(first);
        Fixture.Revive(second);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
        Assert.Same(second, fx.RespawnedPlayers[0]);
    }

    [Fact]
    public void Move_Should_Wait_Without_A_Time_Limit_For_A_Player_Who_Is_Still_Connected()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        fx.Watcher.OnRespawnRequested(player);

        for (int i = 0; i < 100000; i++)
        {
            fx.RunAllScheduled(); // nothing is scheduled, so nothing can time out
        }

        Fixture.Revive(player);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void Move_Should_Skip_A_Player_Who_Died_Again_Before_The_Deferred_Move_Ran()
    {
        var fx = new Fixture();
        var player = fx.Join("alice");
        Fixture.Die(player);
        fx.Watcher.OnRespawnRequested(player);
        Fixture.Revive(player);

        Fixture.Die(player);
        fx.RunAllScheduled();

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void Move_Should_Try_Again_When_Moving_The_Player_Throws_Once()
    {
        var fx = new Fixture { FailuresLeft = 1 };
        var player = fx.Join("alice");
        Fixture.Die(player);
        Fixture.Revive(player);

        fx.Watcher.OnRespawnRequested(player);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
        fx.Logger.Received(1).Error(Arg.Any<string>(), Arg.Any<object[]>());
        Assert.Contains(RespawnWatcher.RetryMs, fx.Delays);
    }

    [Fact]
    public void Move_Should_Stop_And_Leave_The_Failures_In_The_Log_When_Moving_The_Player_Keeps_Throwing()
    {
        var fx = new Fixture { FailuresLeft = int.MaxValue };
        var player = fx.Join("alice");
        Fixture.Die(player);
        Fixture.Revive(player);

        fx.Watcher.OnRespawnRequested(player);
        fx.RunAllScheduled();

        Assert.Empty(fx.Respawned);
        fx.Logger.Received(RespawnWatcher.MaxAttempts).Error(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void Attach_Should_Follow_The_Game_Events_From_Join_To_Respawn()
    {
        var fx = new Fixture();
        var events = Substitute.For<IServerEventAPI>();
        fx.Watcher.Attach(events);
        var player = fx.NewPlayer("alice");

        events.PlayerNowPlaying += Raise.Event<PlayerDelegate>(player);
        Fixture.Die(player);
        events.PlayerRespawn += Raise.Event<PlayerDelegate>(player);
        Fixture.Revive(player);
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void Attach_Should_Forget_A_Player_Who_Disconnects()
    {
        var fx = new Fixture();
        var events = Substitute.For<IServerEventAPI>();
        fx.Watcher.Attach(events);
        var player = fx.NewPlayer("bob");
        events.PlayerNowPlaying += Raise.Event<PlayerDelegate>(player);
        Fixture.Die(player);

        events.PlayerDisconnect += Raise.Event<PlayerDelegate>(player);
        Fixture.Revive(player);
        events.PlayerRespawn += Raise.Event<PlayerDelegate>(player);
        fx.RunAllScheduled();

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void Attach_Should_Reject_A_Missing_Event_Api()
    {
        var fx = new Fixture();

        Assert.Throws<ArgumentNullException>(() => fx.Watcher.Attach(null!));
    }

    [Fact]
    public void Constructor_Should_Reject_Missing_Delegates()
    {
        Assert.Throws<ArgumentNullException>(() => new RespawnWatcher(null!, (_, _) => { }, null));
        Assert.Throws<ArgumentNullException>(() => new RespawnWatcher(_ => true, null!, null));
    }

    private sealed class Fixture
    {
        public Fixture() =>
            Watcher = new RespawnWatcher(
                p =>
                {
                    if (FailuresLeft > 0)
                    {
                        FailuresLeft--;
                        throw new InvalidOperationException("boom");
                    }

                    RespawnedPlayers.Add(p);
                    return true;
                },
                (action, ms) =>
                {
                    Scheduled.Enqueue(action);
                    Delays.Add(ms);
                },
                Logger);

        public RespawnWatcher Watcher { get; }

        public ILogger Logger { get; } = Substitute.For<ILogger>();

        public Queue<Action> Scheduled { get; } = new();

        public List<int> Delays { get; } = [];

        public List<IServerPlayer> RespawnedPlayers { get; } = [];

        public int FailuresLeft { get; set; }

        public List<string> Respawned => RespawnedPlayers.ConvertAll(p => p.PlayerUID);

        /// <summary>Kills the player the way the engine does: its alive setter writes this attribute, and so runs the watchers' listeners.</summary>
        public static void Die(IServerPlayer player) => player.Entity.WatchedAttributes.SetInt("entityDead", 1);

        /// <summary>Revives the player the way the engine does.</summary>
        public static void Revive(IServerPlayer player) => player.Entity.WatchedAttributes.SetInt("entityDead", 0);

        /// <summary>A living player the watcher is not following yet.</summary>
        public IServerPlayer NewPlayer(string uid)
        {
            var entity = Substitute.For<EntityPlayer>();
            var player = Substitute.For<IServerPlayer>();
            player.PlayerUID.Returns(uid);
            player.PlayerName.Returns(uid);
            player.Entity.Returns(entity);
            return player;
        }

        /// <summary>A living player who has joined, so the watcher follows their deaths and revives.</summary>
        public IServerPlayer Join(string uid)
        {
            var player = NewPlayer(uid);
            Watcher.OnNowPlaying(player);
            return player;
        }

        public void RunAllScheduled()
        {
            while (Scheduled.Count > 0)
            {
                Scheduled.Dequeue()();
            }
        }
    }
}
