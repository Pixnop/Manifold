using System;
using System.Collections.Generic;
using Manifold.Api;
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

        // Runtime relight must push to clients (sendToClients: true) and target the dimension's own
        // internal id - a BlockPos built without one defaults to dim 0, which would relight the
        // overworld instead.
        var id = facade.Registry.Get(new AssetLocation("owner:target"))!.InternalId;
        sapi.WorldManager.Received(1).FullRelight(
            Arg.Is<BlockPos>(p => p.dimension == id),
            Arg.Is<BlockPos>(p => p.dimension == id && p.X == 31),
            true);
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

        var order = new List<string>();
        transitions.TryTeleportPlayer(Arg.Any<IServerPlayer>(), Arg.Any<AssetLocation>(), Arg.Any<TransitionOptions>())
            .Returns(true);
        transitions.When(t => t.TryTeleportPlayer(Arg.Any<IServerPlayer>(), Arg.Any<AssetLocation>(), Arg.Any<TransitionOptions>()))
            .Do(_ => order.Add("teleport"));
        facade.Registry.Destroyed += (_, _) => order.Add("destroyed");

        Assert.True(facade.ForceRemoveDimension(new AssetLocation("owner:ephemeral")));

        transitions.Received(1).TryTeleportPlayer(
            occupant,
            Arg.Is<AssetLocation>(c => c.Equals(new AssetLocation("manifold:overworld"))),
            Arg.Is<TransitionOptions>(o => o.SpawnBehavior == SpawnBehavior.LastVisited));
        Assert.Equal(new[] { "teleport", "destroyed" }, order);
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

    [Fact]
    public void GenerateRegion_Should_Throw_When_Dimension_Is_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<ArgumentNullException>(() => facade.GenerateRegion(null!, new BlockPos(0, 0, 0, 0)));
    }

    [Fact]
    public void GenerateRegion_Should_Throw_When_Center_Is_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<ArgumentNullException>(
            () => facade.GenerateRegion(new AssetLocation("owner:target"), null!));
    }

    [Fact]
    public void GenerateRegion_Should_Throw_When_Unhealthy()
    {
        var (facade, _) = NewFacade(healthy: false);
        Assert.Throws<ManifoldUnhealthyException>(
            () => facade.GenerateRegion(new AssetLocation("owner:target"), new BlockPos(0, 0, 0, 0)));
    }

    [Fact]
    public void GenerateRegion_Should_Throw_When_Dimension_Not_Found()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<DimensionNotFoundException>(
            () => facade.GenerateRegion(new AssetLocation("nope:nope"), new BlockPos(0, 0, 0, 0)));
    }

    [Fact]
    public void GenerateRegion_Should_Throw_When_Dimension_Not_Active()
    {
        var (facade, _) = NewFacade(healthy: true);
        var qCode = new AssetLocation("ghost:pending");
        ((DimensionRegistry)facade.Registry).SeedFromManifest(
            new ManifestEntry(qCode, 501, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);

        Assert.Throws<DimensionStateException>(
            () => facade.GenerateRegion(qCode, new BlockPos(0, 0, 0, 0)));
    }

    [Fact]
    public void GenerateRegion_Should_Generate_The_Column_Around_Center_With_No_Player()
    {
        var (facade, sapi) = NewFacade(healthy: true);
        var target = facade.Registry.Get(new AssetLocation("owner:target"))!;

        facade.GenerateRegion(new AssetLocation("owner:target"), new BlockPos(0, 8, 0, 0));

        sapi.WorldManager.Received(1).CreateChunkColumnForDimension(0, 0, target.InternalId);
        sapi.WorldManager.DidNotReceiveWithAnyArgs().ForceSendChunkColumn(default!, default, default, default);
    }

    [Fact]
    public void GetPlayersIn_Should_Throw_When_Dimension_Is_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<ArgumentNullException>(() => facade.GetPlayersIn(null!));
    }

    [Fact]
    public void GetPlayersIn_Should_Throw_When_Dimension_Not_Found()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<DimensionNotFoundException>(() => facade.GetPlayersIn(new AssetLocation("nope:nope")));
    }

    [Fact]
    public void GetPlayersIn_Should_Return_Empty_When_No_One_Is_Inside()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Empty(facade.GetPlayersIn(new AssetLocation("owner:target")));
    }

    [Fact]
    public void GetPlayersIn_Should_Return_Only_Occupants_Of_That_Dimension()
    {
        var (facade, sapi) = NewFacade(healthy: true);
        var target = facade.Registry.Get(new AssetLocation("owner:target"))!;
        var inside = PlayerAt(target.InternalId, "inside");
        var elsewhere = PlayerAt(target.InternalId + 5, "elsewhere");
        sapi.World.AllOnlinePlayers.Returns(new IPlayer[] { inside, elsewhere });

        var players = facade.GetPlayersIn(new AssetLocation("owner:target"));

        Assert.Equal(new[] { inside }, players);
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
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var facade = new ManifoldServerFacade(registry, transitions, sapi, generator, healthy);
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
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var facade = new ManifoldServerFacade(registry, transitions, sapi, generator, isHealthy: true);
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
