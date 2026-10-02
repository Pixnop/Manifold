using System;
using System.Collections.Generic;
using Manifold.Internal;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// <see cref="RespawnWatcher"/>: a respawn request is answered only for a player seen dying, and only
/// once the engine has revived them.
/// </summary>
public sealed class RespawnWatcherTests
{
    [Fact]
    public void OnRespawnRequested_Should_Ignore_A_Player_Who_Never_Died()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: true);

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
        Assert.Empty(fx.Scheduled);
    }

    [Fact]
    public void OnRespawnRequested_Should_Move_A_Dead_Player_At_Once_When_The_Engine_Already_Revived_Them()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);
        player.Entity.Alive = true; // the engine's respawn teleport applied and revived them at once

        fx.Watcher.OnRespawnRequested(player);

        Assert.Equal(["alice"], fx.Respawned);
        Assert.Empty(fx.Scheduled);
    }

    [Fact]
    public void OnRespawnRequested_Should_Wait_For_The_Revive_When_The_Engine_Teleport_Is_Still_Pending()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);

        fx.Watcher.OnRespawnRequested(player);
        fx.RunScheduled();
        fx.RunScheduled();
        Assert.Empty(fx.Respawned);

        player.Entity.Alive = true;
        fx.RunScheduled();

        Assert.Equal(["alice"], fx.Respawned);
        Assert.Empty(fx.Scheduled);
        Assert.All(fx.Delays, d => Assert.Equal(RespawnWatcher.PollIntervalMs, d));
    }

    [Fact]
    public void OnRespawnRequested_Should_Move_The_Player_Only_Once_When_The_Request_Is_Repeated_While_Waiting()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);

        fx.Watcher.OnRespawnRequested(player);
        fx.Watcher.OnRespawnRequested(player);
        Assert.Single(fx.Scheduled);

        player.Entity.Alive = true;
        fx.RunScheduled();

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void OnRespawnRequested_Should_Ignore_A_Request_After_The_Player_Was_Moved_Until_They_Die_Again()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);
        player.Entity.Alive = true;
        fx.Watcher.OnRespawnRequested(player);

        fx.Watcher.OnRespawnRequested(player); // a request from a living player: ignored
        Assert.Single(fx.Respawned);

        fx.Watcher.OnDeath(player);
        fx.Watcher.OnRespawnRequested(player);
        Assert.Equal(2, fx.Respawned.Count);
    }

    [Fact]
    public void OnRespawnRequested_Should_Handle_A_Player_Who_Joined_Dead()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnNowPlaying(player);
        player.Entity.Alive = true;

        fx.Watcher.OnRespawnRequested(player);

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void OnNowPlaying_Should_Not_Mark_A_Living_Player_As_Dead()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: true);
        fx.Watcher.OnNowPlaying(player);

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void OnDisconnect_Should_Stop_The_Wait_For_A_Player_Who_Left()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);
        fx.Watcher.OnRespawnRequested(player);

        fx.Watcher.OnDisconnect(player);
        player.Entity.Alive = true;
        fx.RunScheduled();

        Assert.Empty(fx.Respawned);
        Assert.Empty(fx.Scheduled);
    }

    [Fact]
    public void OnDisconnect_Should_Forget_That_The_Player_Was_Dead()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);
        fx.Watcher.OnDisconnect(player);
        player.Entity.Alive = true;

        fx.Watcher.OnRespawnRequested(player);

        Assert.Empty(fx.Respawned);
    }

    [Fact]
    public void Poll_Should_Drop_A_Stale_Wait_When_The_Player_Came_Back_As_A_New_Connection()
    {
        var fx = new Fixture();
        var first = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(first);
        fx.Watcher.OnRespawnRequested(first);
        fx.Watcher.OnDisconnect(first);

        var second = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnNowPlaying(second);
        fx.Watcher.OnRespawnRequested(second);
        first.Entity.Alive = true;
        second.Entity.Alive = true;
        fx.RunAllScheduled();

        Assert.Equal(["alice"], fx.Respawned);
        Assert.Same(second, fx.RespawnedPlayers[0]);
    }

    [Fact]
    public void Poll_Should_Stop_Without_Moving_Anyone_When_The_Player_Has_No_Entity()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);
        fx.Watcher.OnRespawnRequested(player);
        player.Entity.Returns((Vintagestory.API.Common.EntityPlayer?)null);

        fx.RunScheduled();

        Assert.Empty(fx.Respawned);
        Assert.Empty(fx.Scheduled);
    }

    [Fact]
    public void Poll_Should_Give_Up_And_Warn_When_The_Engine_Never_Revives_The_Player()
    {
        var fx = new Fixture();
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);

        fx.Watcher.OnRespawnRequested(player);
        fx.RunAllScheduled();

        Assert.Empty(fx.Respawned);
        Assert.Equal(RespawnWatcher.MaxPolls, fx.Delays.Count);
        fx.Logger.Received(1).Warning(Arg.Is<string>(m => m.Contains("never revived")), Arg.Any<object[]>());
    }

    [Fact]
    public void Poll_Should_Log_And_Carry_On_When_Moving_The_Player_Throws()
    {
        var fx = new Fixture { RespawnThrows = true };
        var player = fx.NewPlayer("alice", alive: false);
        fx.Watcher.OnDeath(player);
        player.Entity.Alive = true;

        fx.Watcher.OnRespawnRequested(player);

        fx.Logger.Received(1).Error(Arg.Any<string>(), Arg.Any<object[]>());
        Assert.Empty(fx.Scheduled);
    }

    [Fact]
    public void Attach_Should_Follow_The_Game_Events_From_Death_To_Respawn()
    {
        var fx = new Fixture();
        var events = Substitute.For<IServerEventAPI>();
        fx.Watcher.Attach(events);
        var player = fx.NewPlayer("alice", alive: false);

        events.PlayerDeath += Raise.Event<PlayerDeathDelegate>(player, new DamageSource());
        player.Entity.Alive = true;
        events.PlayerRespawn += Raise.Event<PlayerDelegate>(player);

        Assert.Equal(["alice"], fx.Respawned);
    }

    [Fact]
    public void Attach_Should_Follow_A_Player_Who_Joins_Dead_And_One_Who_Leaves()
    {
        var fx = new Fixture();
        var events = Substitute.For<IServerEventAPI>();
        fx.Watcher.Attach(events);
        var joinedDead = fx.NewPlayer("alice", alive: false);
        var left = fx.NewPlayer("bob", alive: false);

        events.PlayerNowPlaying += Raise.Event<PlayerDelegate>(joinedDead);
        events.PlayerNowPlaying += Raise.Event<PlayerDelegate>(left);
        events.PlayerDisconnect += Raise.Event<PlayerDelegate>(left);
        joinedDead.Entity.Alive = true;
        left.Entity.Alive = true;
        events.PlayerRespawn += Raise.Event<PlayerDelegate>(joinedDead);
        events.PlayerRespawn += Raise.Event<PlayerDelegate>(left);

        Assert.Equal(["alice"], fx.Respawned);
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
                    if (RespawnThrows)
                    {
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

        public bool RespawnThrows { get; init; }

        public List<string> Respawned => RespawnedPlayers.ConvertAll(p => p.PlayerUID);

        public IServerPlayer NewPlayer(string uid, bool alive)
        {
            var entity = Substitute.For<EntityPlayer>();
            entity.Alive = alive;
            var player = Substitute.For<IServerPlayer>();
            player.PlayerUID.Returns(uid);
            player.PlayerName.Returns(uid);
            player.Entity.Returns(entity);
            return player;
        }

        public void RunScheduled() => Scheduled.Dequeue()();

        public void RunAllScheduled()
        {
            while (Scheduled.Count > 0)
            {
                RunScheduled();
            }
        }
    }
}
