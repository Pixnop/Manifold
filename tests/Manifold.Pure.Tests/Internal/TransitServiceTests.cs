using System;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Api.Transitions;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class TransitServiceTests
{
    private static readonly string[] LeftThenEntered = ["left", "entered"];

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, player, _, _) = NewService();
        Assert.Throws<DimensionNotFoundException>(
            () => svc.TeleportPlayer(player, Code("nope:nope")));
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_PlayerEntering()
    {
        var (svc, _, player, _, _) = NewService();
        bool entered = false;
        svc.PlayerEntering += (_, _) => entered = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.True(entered);
    }

    [Fact]
    public void TeleportPlayer_Should_Abort_When_PlayerEntering_Cancelled()
    {
        var (svc, _, player, tele, _) = NewService();
        bool entered = false;
        bool left = false;
        svc.PlayerEntering += (_, e) => e.Cancel = true;
        svc.PlayerEntered += (_, _) => entered = true;
        svc.PlayerLeft += (_, _) => left = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.False(entered);
        Assert.False(left);
        tele.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_Left_Then_Entered_In_Order()
    {
        var (svc, _, player, _, _) = NewService();
        var order = new System.Collections.Generic.List<string>();
        svc.PlayerLeft += (_, _) => order.Add("left");
        svc.PlayerEntered += (_, _) => order.Add("entered");
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(LeftThenEntered, order);
    }

    [Fact]
    public void TeleportPlayer_Should_Call_Teleporter_When_Not_Cancelled()
    {
        var (svc, _, player, tele, _) = NewService();
        svc.TeleportPlayer(player, Code("owner:target"));
        tele.Received(1).Teleport(player, Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Manifold_Unhealthy()
    {
        var (svc, _, player, _, _) = NewService();
        svc.MarkUnhealthy();
        Assert.Throws<ManifoldUnhealthyException>(
            () => svc.TeleportPlayer(player, Code("owner:target")));
    }

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, player, _, _) = NewService();
        var qCode = Code("ghost:dim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 99, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        Assert.Throws<DimensionStateException>(
            () => svc.TeleportPlayer(player, qCode));
    }

    [Fact]
    public void TeleportEntity_Should_Throw_When_Entity_Is_A_Player()
    {
        var (svc, _, _, _, _) = NewService();
        var playerEntity = Substitute.For<EntityPlayer>();
        Assert.Throws<ArgumentException>(() => svc.TeleportEntity(playerEntity, Code("owner:target")));
    }

    [Fact]
    public void TeleportEntity_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, _, _, _) = NewService();
        var entity = Substitute.For<Entity>();
        Assert.Throws<DimensionNotFoundException>(() => svc.TeleportEntity(entity, Code("nope:nope")));
    }

    [Fact]
    public void TeleportEntity_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, _, _, _) = NewService();
        var qCode = Code("ghost:edim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 98, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        var entity = Substitute.For<Entity>();
        Assert.Throws<DimensionStateException>(() => svc.TeleportEntity(entity, qCode));
    }

    [Fact]
    public void TeleportEntity_Should_Call_Mover_With_Resolved_Position()
    {
        var (svc, _, _, _, mover) = NewService();
        var entity = Substitute.For<Entity>();
        svc.TeleportEntity(entity, Code("owner:target"));
        mover.Received(1).Move(entity, Arg.Any<BlockPos>());
    }

    private static AssetLocation Code(string s) => new(s);

    private static (TransitService Service, DimensionRegistry Registry, IServerPlayer Player, IPlayerTeleporter Teleporter, IEntityMover Mover)
        NewService()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:target"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var teleporter = Substitute.For<IPlayerTeleporter>();
        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(100, 100, 100, 10));

        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var mover = Substitute.For<IEntityMover>();
        var svc = new TransitService(registry, sapi, teleporter, positionResolver, generator, new PlayerPositionStore(), new InventorySwapper(sapi), mover);

        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());

        return (svc, registry, player, teleporter, mover);
    }
}
