using System;
using Manifold.Api;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class ManifoldServerFacadeTests
{
    [Fact]
    public void RelightRegion_Should_Throw_When_Dimension_Is_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<ArgumentNullException>(
            () => facade.RelightRegion(null!, new BlockPos(0, 0, 0, 0), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void RelightRegion_Should_Throw_When_Bounds_Are_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        var code = new AssetLocation("owner:target");
        Assert.Throws<ArgumentNullException>(
            () => facade.RelightRegion(code, null!, new BlockPos(1, 1, 1, 0)));
        Assert.Throws<ArgumentNullException>(
            () => facade.RelightRegion(code, new BlockPos(0, 0, 0, 0), null!));
    }

    [Fact]
    public void RelightRegion_Should_Throw_When_Unhealthy()
    {
        var (facade, _) = NewFacade(healthy: false);
        Assert.Throws<ManifoldUnhealthyException>(
            () => facade.RelightRegion(
                new AssetLocation("owner:target"), new BlockPos(0, 0, 0, 0), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void RelightRegion_Should_Throw_When_Dimension_Not_Found()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<DimensionNotFoundException>(
            () => facade.RelightRegion(
                new AssetLocation("nope:nope"), new BlockPos(0, 0, 0, 0), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void RelightRegion_Should_Call_FullRelight_For_Known_Dimension()
    {
        var (facade, sapi) = NewFacade(healthy: true);

        facade.RelightRegion(
            new AssetLocation("owner:target"), new BlockPos(0, 0, 0, 0), new BlockPos(31, 64, 31, 0));

        // Runtime relight must push to clients (sendToClients: true) or the change is invisible.
        sapi.WorldManager.Received(1).FullRelight(Arg.Any<BlockPos>(), Arg.Any<BlockPos>(), true);
    }

    [Fact]
    public void ForceRemove_Should_Throw_When_Dimension_Is_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<ArgumentNullException>(() => facade.ForceRemoveDimension(null!));
    }

    [Fact]
    public void ForceRemove_Should_Throw_When_Unhealthy()
    {
        var (facade, _) = NewFacade(healthy: false);
        Assert.Throws<ManifoldUnhealthyException>(
            () => facade.ForceRemoveDimension(new AssetLocation("owner:ephemeral")));
    }

    [Fact]
    public void ForceRemove_Should_Return_False_When_Not_Found()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.False(facade.ForceRemoveDimension(new AssetLocation("nope:nope")));
    }

    [Fact]
    public void ForceRemove_Should_Remove_Empty_Ephemeral_Dimension()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.True(facade.ForceRemoveDimension(new AssetLocation("owner:ephemeral")));
        Assert.Null(facade.Registry.Get(new AssetLocation("owner:ephemeral")));
    }

    [Fact]
    public void ForceRemove_Should_Throw_When_Target_Is_Persistent()
    {
        // Lifetime immutability wins: persistent dimensions cannot be force-removed (no evacuation).
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<DimensionStateException>(
            () => facade.ForceRemoveDimension(new AssetLocation("owner:target")));
    }

    [Fact]
    public void ForceRemove_Should_Teleport_Occupant_To_Overworld_Then_Remove()
    {
        var (facade, transitions, dimId) = NewOccupiedFacade(out var sapi);
        var occupant = PlayerAt(dimId, "occupant");
        sapi.World.AllOnlinePlayers.Returns(new IPlayer[] { occupant });

        Assert.True(facade.ForceRemoveDimension(new AssetLocation("owner:ephemeral")));

        // The only call to the transit substitute is the occupant evacuation. Counting ReceivedCalls
        // proves the occupant was teleported, without the matcher plumbing around the struct param.
        Assert.Single(transitions.ReceivedCalls());
        Assert.Null(facade.Registry.Get(new AssetLocation("owner:ephemeral")));
    }

    [Fact]
    public void ForceRemove_Should_Not_Teleport_Players_In_Other_Dimensions()
    {
        var (facade, transitions, dimId) = NewOccupiedFacade(out var sapi);
        var elsewhere = PlayerAt(dimId + 5, "elsewhere");
        sapi.World.AllOnlinePlayers.Returns(new IPlayer[] { elsewhere });

        facade.ForceRemoveDimension(new AssetLocation("owner:ephemeral"));

        // A player in a different dimension must not be evacuated, so no transit call at all.
        Assert.Empty(transitions.ReceivedCalls());
    }

    private static (ManifoldServerFacade Facade, ICoreServerAPI Sapi) NewFacade(bool healthy)
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        registry.DefineForOwner(new AssetLocation("owner:target"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();
        registry.DefineForOwner(new AssetLocation("owner:ephemeral"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();

        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.World.AllOnlinePlayers.Returns(System.Array.Empty<IPlayer>());
        var transitions = Substitute.For<Manifold.Api.Server.ITransitionService>();
        var facade = new ManifoldServerFacade(registry, transitions, sapi, healthy);
        return (facade, sapi);
    }

    // Healthy facade with a single ephemeral dimension and an exposed transit substitute, for the
    // occupant-evacuation tests. The registry has no occupancy predicate (the facade tests exercise
    // ForceRemove's evacuation, not the registry guard, which is covered in DimensionRegistryTests).
    private static (ManifoldServerFacade Facade, Manifold.Api.Server.ITransitionService Transitions, int EphemeralId) NewOccupiedFacade(out ICoreServerAPI sapi)
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        var ephemeral = registry.DefineForOwner(new AssetLocation("owner:ephemeral"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();

        sapi = Substitute.For<ICoreServerAPI>();
        var transitions = Substitute.For<Manifold.Api.Server.ITransitionService>();
        var facade = new ManifoldServerFacade(registry, transitions, sapi, isHealthy: true);
        return (facade, transitions, ephemeral.InternalId);
    }

    private static IServerPlayer PlayerAt(int dimension, string uid)
    {
        // Entity.Pos is not a virtual member, so it cannot be stubbed; the substitute carries a real
        // EntityPos (created by the entity constructor) whose Dimension we set directly. EntityPosAccess
        // reads that same instance via reflection.
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = dimension;
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(entity);
        player.PlayerUID.Returns(uid);
        return player;
    }
}
