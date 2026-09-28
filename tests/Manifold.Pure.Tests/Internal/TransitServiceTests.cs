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
    private static readonly string[] EnteringThenArriving = ["entering", "arriving"];

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        Assert.Throws<DimensionNotFoundException>(
            () => svc.TeleportPlayer(player, Code("nope:nope")));
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_PlayerEntering()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        bool entered = false;
        svc.PlayerEntering += (_, _) => entered = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.True(entered);
    }

    [Fact]
    public void TeleportPlayer_Should_Abort_When_PlayerEntering_Cancelled()
    {
        var (svc, _, tele, _, _) = NewService();
        var player = NewPlayer();
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
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        var order = new System.Collections.Generic.List<string>();
        svc.PlayerLeft += (_, _) => order.Add("left");
        svc.PlayerEntered += (_, _) => order.Add("entered");
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(LeftThenEntered, order);
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_PlayerArriving_With_Final_Position()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        PlayerArrivingDimensionEventArgs? captured = null;
        svc.PlayerArriving += (_, e) => captured = e;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.NotNull(captured);
        Assert.Same(player, captured!.Player);
        Assert.Equal("owner:target", captured.TargetDimension.Code.ToString());
        Assert.Equal(new BlockPos(100, 100, 100, captured.TargetDimension.InternalId), captured.TargetPosition);
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_PlayerEntered_With_Landing_Position()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        PlayerEnteredDimensionEventArgs? captured = null;
        svc.PlayerEntered += (_, e) => captured = e;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.NotNull(captured);
        Assert.Equal(new BlockPos(100, 100, 100, captured!.TargetDimension.InternalId), captured.TargetPosition);
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_Entering_Then_Arriving_In_Order()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        var order = new System.Collections.Generic.List<string>();
        svc.PlayerEntering += (_, _) => order.Add("entering");
        svc.PlayerArriving += (_, _) => order.Add("arriving");
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(EnteringThenArriving, order);
    }

    [Fact]
    public void TeleportPlayer_Should_Abort_When_PlayerArriving_Cancelled()
    {
        var (svc, _, tele, _, _) = NewService();
        var player = NewPlayer();
        bool entered = false;
        bool left = false;
        svc.PlayerArriving += (_, e) => e.Cancel = true;
        svc.PlayerEntered += (_, _) => entered = true;
        svc.PlayerLeft += (_, _) => left = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.False(entered);
        Assert.False(left);
        tele.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Log_CancellationReason_When_PlayerArriving_Cancels()
    {
        var (svc, _, _, _, _, sapi) = NewServiceWithApi();
        var player = NewPlayer();
        const string reason = "vetoed by a subscriber";
        svc.PlayerArriving += (_, e) =>
        {
            e.Cancel = true;
            e.CancellationReason = reason;
        };

        svc.TeleportPlayer(player, Code("owner:target"));

        // PlayerArrivingDimensionEventArgs.CancellationReason is documented as reported via the
        // transit service log; nothing read it before this fix.
        sapi.Logger.Received(1).Notification(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void TeleportPlayer_Should_Log_When_PlayerEntering_Cancels()
    {
        var (svc, _, _, _, _, sapi) = NewServiceWithApi();
        var player = NewPlayer();
        svc.PlayerEntering += (_, e) => e.Cancel = true;

        svc.TeleportPlayer(player, Code("owner:target"));

        sapi.Logger.Received(1).Notification(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void TeleportPlayer_Should_Call_Teleporter_When_Not_Cancelled()
    {
        var (svc, _, tele, _, _) = NewService();
        var player = NewPlayer();
        svc.TeleportPlayer(player, Code("owner:target"));
        tele.Received(1).Teleport(player, Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, _, _, _) = NewService();
        var player = NewPlayer();
        var qCode = Code("ghost:dim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 99, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        Assert.Throws<DimensionStateException>(
            () => svc.TeleportPlayer(player, qCode));
    }

    [Fact]
    public void TryTeleportPlayer_Should_Return_True_When_Not_Cancelled()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        Assert.True(svc.TryTeleportPlayer(player, Code("owner:target")));
    }

    [Fact]
    public void TryTeleportPlayer_Should_Return_False_When_PlayerEntering_Cancels()
    {
        var (svc, _, tele, _, _) = NewService();
        var player = NewPlayer();
        svc.PlayerEntering += (_, e) => e.Cancel = true;

        Assert.False(svc.TryTeleportPlayer(player, Code("owner:target")));
        tele.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TryTeleportPlayer_Should_Return_False_When_PlayerArriving_Cancels()
    {
        var (svc, _, tele, _, _) = NewService();
        var player = NewPlayer();
        svc.PlayerArriving += (_, e) => e.Cancel = true;

        Assert.False(svc.TryTeleportPlayer(player, Code("owner:target")));
        tele.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TryTeleportPlayer_Should_Throw_Same_Exceptions_As_TeleportPlayer()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        Assert.Throws<DimensionNotFoundException>(() => svc.TryTeleportPlayer(player, Code("nope:nope")));
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
        var (svc, registry, _, mover, _) = NewService();
        var entity = Substitute.For<Entity>();
        var target = registry.Get(Code("owner:target"))!;
        svc.TeleportEntity(entity, Code("owner:target"));
        mover.Received(1).Move(
            entity,
            Arg.Is<BlockPos>(p => p.X == 100 && p.Y == 100 && p.Z == 100 && p.dimension == target.InternalId));
    }

    [Fact]
    public void TeleportEntity_Should_Raise_EntityChangedDimension_With_Final_Position()
    {
        var (svc, _, _, _, _) = NewService();
        var entity = Substitute.For<Entity>();
        EntityChangedDimensionEventArgs? captured = null;
        svc.EntityChangedDimension += (_, e) => captured = e;

        svc.TeleportEntity(entity, Code("owner:target"));

        Assert.NotNull(captured);
        Assert.Same(entity, captured!.Entity);
        Assert.Equal("owner:target", captured.NewDimension.Code.ToString());
        Assert.Equal(new BlockPos(100, 100, 100, captured.NewDimension.InternalId), captured.NewPosition);
    }

    [Fact]
    public void TeleportEntity_Should_Not_Raise_EntityChangedDimension_When_Mover_Throws()
    {
        var (svc, _, _, mover, _) = NewService();
        var entity = Substitute.For<Entity>();
        mover.When(m => m.Move(Arg.Any<Entity>(), Arg.Any<BlockPos>()))
             .Do(_ => throw new InvalidOperationException("boom"));
        bool raised = false;
        svc.EntityChangedDimension += (_, _) => raised = true;

        Assert.Throws<InvalidOperationException>(() => svc.TeleportEntity(entity, Code("owner:target")));
        Assert.False(raised);
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, _, _, _) = NewService();
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.Throws<DimensionNotFoundException>(() => svc.TeleportBlock(src, Code("nope:nope"), dst));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, _, _, _) = NewService();
        var qCode = Code("ghost:bdim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 97, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.Throws<DimensionStateException>(() => svc.TeleportBlock(src, qCode, dst));
    }

    [Fact]
    public void TeleportBlock_Should_Call_Mover_With_Dim_Encoded_Target()
    {
        var (svc, registry, _, _, blockMover) = NewService();
        var target = registry.Get(Code("owner:target"))!;
        var src = new BlockPos(10, 64, 10, 0);
        var dst = new BlockPos(20, 64, 30, 0); // dim 0; service should overwrite to target.InternalId
        svc.TeleportBlock(src, Code("owner:target"), dst);
        blockMover.Received(1).Move(
            src,
            Arg.Is<BlockPos>(p => p.X == 20 && p.Y == 64 && p.Z == 30 && p.dimension == target.InternalId));
    }

    [Fact]
    public void TeleportBlock_Should_Not_Mutate_Caller_TargetPos()
    {
        var (svc, _, _, _, _) = NewService();
        var src = new BlockPos(10, 64, 10, 0);
        var dst = new BlockPos(20, 64, 30, 0);
        svc.TeleportBlock(src, Code("owner:target"), dst);
        Assert.Equal(0, dst.dimension);
    }

    [Fact]
    public void TeleportBlock_Should_Return_Mover_Result()
    {
        var (svc, _, _, _, blockMover) = NewService();
        blockMover.Move(Arg.Any<BlockPos>(), Arg.Any<BlockPos>()).Returns(false);
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.False(svc.TeleportBlock(src, Code("owner:target"), dst));
    }

    [Fact]
    public void TeleportBlock_Should_Return_False_And_Not_Call_Mover_When_Source_Is_MultiPosition()
    {
        var (svc, _, _, _, blockMover, sapi) = NewServiceWithApi();
        blockMover.IsMultiPosition(Arg.Any<BlockPos>(), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = "one half of a two-block bed";
                return true;
            });
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);

        bool moved = svc.TeleportBlock(src, Code("owner:target"), dst);

        Assert.False(moved);
        blockMover.DidNotReceive().Move(Arg.Any<BlockPos>(), Arg.Any<BlockPos>());
        sapi.Logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void TeleportBlock_Should_Call_Mover_When_Source_Is_Not_MultiPosition()
    {
        var (svc, _, _, _, blockMover) = NewService();
        blockMover.IsMultiPosition(Arg.Any<BlockPos>(), out Arg.Any<string?>()).Returns(false);
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);

        svc.TeleportBlock(src, Code("owner:target"), dst);

        blockMover.Received(1).Move(src, Arg.Any<BlockPos>());
    }

    [Fact]
    public void IsMultiPositionBlock_Should_Delegate_To_BlockMover()
    {
        var (svc, _, _, _, blockMover) = NewService();
        var pos = new BlockPos(1, 64, 1, 0);
        blockMover.IsMultiPosition(pos, out Arg.Any<string?>()).Returns(true);

        Assert.True(svc.IsMultiPositionBlock(pos));
    }

    [Fact]
    public void IsMultiPositionBlock_Should_Throw_When_Pos_Is_Null()
    {
        var (svc, _, _, _, _) = NewService();
        Assert.Throws<ArgumentNullException>(() => svc.IsMultiPositionBlock(null!));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Source_Is_Null()
    {
        var (svc, _, _, _, _) = NewService();
        Assert.Throws<ArgumentNullException>(() => svc.TeleportBlock(null!, Code("owner:target"), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_TargetLocal_Is_Null()
    {
        var (svc, _, _, _, _) = NewService();
        Assert.Throws<ArgumentNullException>(() => svc.TeleportBlock(new BlockPos(1, 1, 1, 0), Code("owner:target"), null!));
    }

    [Fact]
    public void TeleportPlayer_Should_Continue_When_A_PlayerEntering_Subscriber_Throws()
    {
        var (svc, _, tele, _, _) = NewService();
        var player = NewPlayer();
        svc.PlayerEntering += (_, _) => throw new InvalidOperationException("rogue mod");
        bool entered = false;
        svc.PlayerEntered += (_, _) => entered = true;

        // A throwing third-party subscriber must not abort the transit pipeline for the initiator.
        svc.TeleportPlayer(player, Code("owner:target"));

        tele.Received(1).Teleport(player, Arg.Any<BlockPos>());
        Assert.True(entered);
    }

    [Fact]
    public void TeleportPlayer_Should_Isolate_A_Throwing_PlayerEntered_Subscriber()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        bool goodRan = false;
        svc.PlayerEntered += (_, _) => throw new InvalidOperationException("rogue mod");
        svc.PlayerEntered += (_, _) => goodRan = true;

        svc.TeleportPlayer(player, Code("owner:target"));

        Assert.True(goodRan);
    }

    [Fact]
    public void TeleportEntity_Should_Isolate_A_Throwing_EntityChangedDimension_Subscriber()
    {
        var (svc, _, _, _, _) = NewService();
        var entity = Substitute.For<Entity>();
        bool goodRan = false;
        svc.EntityChangedDimension += (_, _) => throw new InvalidOperationException("rogue mod");
        svc.EntityChangedDimension += (_, _) => goodRan = true;

        svc.TeleportEntity(entity, Code("owner:target"));

        Assert.True(goodRan);
    }

    [Fact]
    public void TeleportPlayer_Should_Persist_Inventory_Store_Even_When_A_Swap_Throws()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:sep"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithSeparateInventory(ManifoldInventory.Hotbar)
            .RegisterStatic();

        var teleporter = Substitute.For<IPlayerTeleporter>();
        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(100, 100, 100, 10));
        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());

        // First visit to a separated dimension clears the destination set; simulate the engine
        // inventory call blowing up mid-swap (e.g. a malformed snapshot from a removed item).
        var swapper = Substitute.For<IInventorySwapper>();
        swapper.Serialize(Arg.Any<IServerPlayer>(), Arg.Any<ManifoldInventory>())
            .Returns(new byte[] { 1, 2, 3 });
        swapper.When(s => s.Clear(Arg.Any<IServerPlayer>(), Arg.Any<ManifoldInventory>()))
            .Do(_ => throw new InvalidOperationException("inventory engine blew up"));

        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), NewDismounter()),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            swapper);

        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());
        player.GetModdata("manifold:inv").Returns((byte[]?)null); // first visit

        // The swap throws, but the just-captured original snapshot must still be persisted so the
        // player's items are recoverable on the next transit (no silent permanent item loss).
        Assert.Throws<InvalidOperationException>(() => svc.TeleportPlayer(player, Code("owner:sep")));
        player.Received(1).SetModdata("manifold:inv", Arg.Any<byte[]>());
    }

    [Fact]
    public void TeleportPlayer_Should_Skip_Swap_And_Preserve_Raw_Moddata_When_Inventory_Profile_Is_Corrupt()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:sep"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithSeparateInventory(ManifoldInventory.Hotbar)
            .RegisterStatic();

        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(100, 100, 100, 10));
        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var teleporter = Substitute.For<IPlayerTeleporter>();

        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), NewDismounter()),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            Substitute.For<IInventorySwapper>());

        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());
        var corrupt = new byte[] { 0xFF, 0x01, 0x02 };
        player.GetModdata("manifold:inv").Returns(corrupt);

        // The transit itself must still complete - a corrupt inventory profile is not a reason to
        // abort the whole transit, only to skip the swap.
        svc.TeleportPlayer(player, Code("owner:sep"));

        teleporter.Received(1).Teleport(player, Arg.Any<BlockPos>());
        player.DidNotReceive().SetModdata("manifold:inv", Arg.Any<byte[]>());
        player.Received(1).SetModdata("manifold:inv.corrupt", corrupt);
    }

    [Fact]
    public void TeleportPlayer_Should_Write_The_Inventory_Schema_Sidecar_Entry_After_A_Swap()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:sep"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithSeparateInventory(ManifoldInventory.Hotbar)
            .RegisterStatic();

        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(100, 100, 100, 10));
        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var swapper = Substitute.For<IInventorySwapper>();
        swapper.Serialize(Arg.Any<IServerPlayer>(), Arg.Any<ManifoldInventory>()).Returns(new byte[] { 1 });

        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(Substitute.For<IPlayerTeleporter>(), Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), NewDismounter()),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            swapper);

        var player = NewPlayer();
        var moddata = BackModdata(player);

        svc.TeleportPlayer(player, Code("owner:sep"));

        Assert.Equal(1, SchemaSidecar.Load(moddata["manifold:schema"]).GetVersion("manifold:inv"));
    }

    [Fact]
    public void TeleportPlayer_Should_Refuse_An_Unrecognized_Future_Inventory_Version()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:sep"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithSeparateInventory(ManifoldInventory.Hotbar)
            .RegisterStatic();

        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(100, 100, 100, 10));
        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var swapper = Substitute.For<IInventorySwapper>();
        var teleporter = Substitute.For<IPlayerTeleporter>();

        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), NewDismounter()),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            swapper);

        var player = NewPlayer();
        var moddata = BackModdata(player);

        // A payload that IS valid for the current parser (a real store), so refusal is provably
        // driven by the version check, not by a garbage payload hitting the corrupt-data path.
        var seed = new PlayerInventoryStore();
        seed.SetCurrentKey(ManifoldInventory.Hotbar, "mod:vault");
        var raw = seed.ToBytes();
        moddata["manifold:inv"] = raw;
        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:inv", 99);
        moddata["manifold:schema"] = sidecar.ToBytes();

        svc.TeleportPlayer(player, Code("owner:sep"));

        // Transit itself still completes; only the inventory swap is skipped.
        teleporter.Received(1).Teleport(player, Arg.Any<BlockPos>());
        swapper.DidNotReceive().Serialize(Arg.Any<IServerPlayer>(), Arg.Any<ManifoldInventory>());
        swapper.DidNotReceive().Clear(Arg.Any<IServerPlayer>(), Arg.Any<ManifoldInventory>());
        Assert.Equal(raw, moddata["manifold:inv"]);
        Assert.Equal(raw, moddata["manifold:inv.unrecognized"]);
        Assert.Equal(99, SchemaSidecar.Load(moddata["manifold:schema"]).GetVersion("manifold:inv")); // never downgraded
        sapi.Logger.Received(1).Error(
            Arg.Any<string>(),
            Arg.Is<object[]>(a => a.Contains("manifold:inv") && a.Contains(99) && a.Contains(1)));
    }

    [Fact]
    public void TeleportPlayer_Should_Not_Mutate_Callers_OverridePosition()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        var overridePos = new BlockPos(5, 6, 7, 0);

        svc.TeleportPlayer(player, Code("owner:target"), new TransitionOptions { OverridePosition = overridePos });

        Assert.Equal(0, overridePos.dimension);
    }

    [Fact]
    public void TeleportEntity_Should_Not_Mutate_Callers_OverridePosition()
    {
        var (svc, _, _, _, _) = NewService();
        var entity = Substitute.For<Entity>();
        var overridePos = new BlockPos(5, 6, 7, 0);

        svc.TeleportEntity(entity, Code("owner:target"), new TransitionOptions { OverridePosition = overridePos });

        Assert.Equal(0, overridePos.dimension);
    }

    [Fact]
    public void TeleportPlayer_Should_Dismount_The_Player_Before_Teleporting()
    {
        var (svc, _, teleporter, _, _, dismounter) = NewServiceWithDismounter();
        var player = NewPlayer();

        svc.TeleportPlayer(player, Code("owner:target"));

        // The mount must be released before the player's entity moves to the target dimension -
        // never after, or the player would briefly be flagged as mounted on an entity that never
        // left the source dimension.
        Received.InOrder(() =>
        {
            dismounter.Dismount(player);
            teleporter.Teleport(player, Arg.Any<BlockPos>());
        });
    }

    [Fact]
    public void TeleportPlayer_Should_Not_Dismount_When_The_Transit_Is_Cancelled()
    {
        var (svc, _, _, _, _, dismounter) = NewServiceWithDismounter();
        var player = NewPlayer();
        svc.PlayerArriving += (_, e) => e.Cancel = true;

        svc.TeleportPlayer(player, Code("owner:target"));

        // A player who never left must not be forcibly dismounted from whatever they were riding.
        dismounter.DidNotReceive().Dismount(Arg.Any<IServerPlayer>());
    }

    [Fact]
    public void TryTeleportPlayer_Should_Abort_And_Return_False_When_The_Mount_Refuses_To_Release_The_Player()
    {
        var (svc, _, teleporter, _, _, dismounter) = NewServiceWithDismounter();
        var player = NewPlayer();
        dismounter.Dismount(player).Returns(false);

        bool moved = svc.TryTeleportPlayer(player, Code("owner:target"));

        // Nothing has moved yet at the dismount step, so a refusal must abort the whole transit,
        // exactly like a PlayerArriving cancellation: not teleport the player and drag their mount
        // to the target dimension's coordinates in the source dimension underneath them.
        Assert.False(moved);
        teleporter.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Fall_Back_To_Default_Resolver_When_DimensionSpawn_Has_No_Spawn_Point()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:nospawn"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithSpawnBehavior(SpawnBehavior.DimensionSpawn) // no WithFixedSpawn: no configured spawn point
            .RegisterStatic();

        var sapi = Substitute.For<ICoreServerAPI>();
        var defaultResolver = Substitute.For<ITargetPositionResolver>();
        defaultResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(1, 2, 3, 0));
        var teleporter = Substitute.For<IPlayerTeleporter>();
        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), NewDismounter()),
            defaultResolver,
            new DimensionGenerator(registry, new GeneratedColumnStore()),
            new PlayerPositionStore(),
            Substitute.For<IInventorySwapper>());

        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());

        // Must not throw and must not land at the magic (0, 64, 0) - it falls back to the default
        // resolver instead.
        svc.TeleportPlayer(player, Code("owner:nospawn"));

        teleporter.Received(1).Teleport(player, Arg.Is<BlockPos>(p => p.X == 1 && p.Y == 2 && p.Z == 3));
        sapi.Logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void TeleportPlayer_Should_Warn_Only_Once_Per_Dimension_For_Missing_DimensionSpawn()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:nospawn"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithSpawnBehavior(SpawnBehavior.DimensionSpawn)
            .RegisterStatic();

        var sapi = Substitute.For<ICoreServerAPI>();
        var defaultResolver = Substitute.For<ITargetPositionResolver>();
        defaultResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(1, 2, 3, 0));
        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(Substitute.For<IPlayerTeleporter>(), Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), NewDismounter()),
            defaultResolver,
            new DimensionGenerator(registry, new GeneratedColumnStore()),
            new PlayerPositionStore(),
            Substitute.For<IInventorySwapper>());

        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());

        svc.TeleportPlayer(player, Code("owner:nospawn"));
        svc.TeleportPlayer(player, Code("owner:nospawn"));

        sapi.Logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void TeleportPlayer_Should_Restore_Previous_GameMode_When_Leaving_A_Forced_Dimension()
    {
        var (svc, registry, _, _, _) = NewService();
        var player = NewPlayer();
        RegisterForced(registry, "owner:creative", EnumGameMode.Creative);
        var moddata = BackModdata(player);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;

        svc.TeleportPlayer(player, Code("owner:creative"));
        Assert.Equal(EnumGameMode.Creative, player.WorldData.CurrentGameMode);

        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(EnumGameMode.Survival, player.WorldData.CurrentGameMode);
        Assert.Empty(moddata);
    }

    [Fact]
    public void TeleportPlayer_Should_Match_The_0_5_1_Released_GameMode_Format_And_Record_Schema_Version_1()
    {
        // Golden bytes: the saved value is the raw 4-byte int, no envelope, unchanged since v0.5.1
        // (git show v0.5.1:src/Manifold/Internal/TransitService.cs). The sidecar records version 1
        // for it, the same way DimensionPersistence.Save does for the manifest.
        var (svc, registry, _, _, _) = NewService();
        var player = NewPlayer();
        RegisterForced(registry, "owner:creative", EnumGameMode.Creative);
        var moddata = BackModdata(player);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;

        svc.TeleportPlayer(player, Code("owner:creative"));

        Assert.Equal(BitConverter.GetBytes((int)EnumGameMode.Survival), moddata["manifold:gamemode-before-forced"]);
        Assert.Equal(1, SchemaSidecar.Load(moddata["manifold:schema"]).GetVersion("manifold:gamemode-before-forced"));
    }

    [Fact]
    public void TeleportPlayer_Should_Restore_GameMode_From_A_Blob_With_No_Sidecar_Entry()
    {
        // A player who left a forced dimension on a build that predates this sidecar and only
        // reconnects now: the saved value is there, but "manifold:schema" has no entry for it.
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        var moddata = BackModdata(player);
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        moddata["manifold:gamemode-before-forced"] = BitConverter.GetBytes((int)EnumGameMode.Survival);

        svc.TeleportPlayer(player, Code("owner:target")); // unforced

        Assert.Equal(EnumGameMode.Survival, player.WorldData.CurrentGameMode);
        Assert.DoesNotContain("manifold:gamemode-before-forced", moddata.Keys);
    }

    [Fact]
    public void TeleportPlayer_Should_Leave_An_Unrecognized_Future_GameMode_Version_Untouched()
    {
        var (svc, _, _, _, _, sapi) = NewServiceWithApi();
        var player = NewPlayer();
        var moddata = BackModdata(player);
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        var saved = BitConverter.GetBytes((int)EnumGameMode.Survival);
        moddata["manifold:gamemode-before-forced"] = saved;
        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:gamemode-before-forced", 99);
        moddata["manifold:schema"] = sidecar.ToBytes();

        svc.TeleportPlayer(player, Code("owner:target")); // unforced: would normally restore and clear the key

        // Refused: the mode is left alone rather than decoded wrong, the saved blob is kept
        // untouched (not cleared), a copy is preserved under the recovery key, and the sidecar
        // entry is not downgraded back to 1.
        Assert.Equal(EnumGameMode.Creative, player.WorldData.CurrentGameMode);
        Assert.Equal(saved, moddata["manifold:gamemode-before-forced"]);
        Assert.Equal(saved, moddata["manifold:gamemode-before-forced.unrecognized"]);
        Assert.Equal(99, SchemaSidecar.Load(moddata["manifold:schema"]).GetVersion("manifold:gamemode-before-forced"));
        sapi.Logger.Received(1).Error(
            Arg.Any<string>(),
            Arg.Is<object[]>(a => a.Contains("manifold:gamemode-before-forced") && a.Contains(99) && a.Contains(1)));
    }

    [Fact]
    public void TeleportPlayer_Should_Keep_The_Original_GameMode_When_Chaining_Forced_Dimensions()
    {
        var (svc, registry, _, _, _) = NewService();
        var player = NewPlayer();
        RegisterForced(registry, "owner:creative", EnumGameMode.Creative);
        RegisterForced(registry, "owner:spectate", EnumGameMode.Spectator);
        BackModdata(player);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;

        svc.TeleportPlayer(player, Code("owner:creative"));
        svc.TeleportPlayer(player, Code("owner:spectate"));
        Assert.Equal(EnumGameMode.Spectator, player.WorldData.CurrentGameMode);

        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(EnumGameMode.Survival, player.WorldData.CurrentGameMode);
    }

    [Fact]
    public void TeleportPlayer_Should_Leave_GameMode_Alone_Between_Unforced_Dimensions()
    {
        var (svc, _, _, _, _) = NewService();
        var player = NewPlayer();
        var moddata = BackModdata(player);
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;

        svc.TeleportPlayer(player, Code("owner:target"));

        Assert.Equal(EnumGameMode.Creative, player.WorldData.CurrentGameMode);
        Assert.DoesNotContain("manifold:gamemode-before-forced", moddata.Keys);
    }

    private static void RegisterForced(DimensionRegistry registry, string code, EnumGameMode mode) =>
        registry.DefineForOwner(Code(code), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithForcedGameMode(mode)
            .RegisterStatic();

    /// <summary>Backs the player's moddata with a dictionary: a bare substitute returns an empty array, not null.</summary>
    private static System.Collections.Generic.Dictionary<string, byte[]> BackModdata(IServerPlayer player)
    {
        var store = new System.Collections.Generic.Dictionary<string, byte[]>();
        player.GetModdata(Arg.Any<string>()).Returns(c => store.TryGetValue(c.Arg<string>(), out var v) ? v : null);
        player.When(p => p.SetModdata(Arg.Any<string>(), Arg.Any<byte[]>())).Do(c => store[c.ArgAt<string>(0)] = c.ArgAt<byte[]>(1));
        player.When(p => p.RemoveModdata(Arg.Any<string>())).Do(c => store.Remove(c.Arg<string>()));
        return store;
    }

    private static AssetLocation Code(string s) => new(s);

    /// <summary>
    /// A substitute player, for the tests that actually teleport one. NewService/NewServiceWithApi
    /// no longer create one themselves, so tests exercising entity/block transit (which never touch
    /// IServerPlayer) run on any VS version - IPlayer gained an internal abstract member in 1.22.4+
    /// that NSubstitute cannot proxy.
    /// </summary>
    private static IServerPlayer NewPlayer()
    {
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());
        return player;
    }

    private static (TransitService Service, DimensionRegistry Registry, IPlayerTeleporter Teleporter, IEntityMover EntityMover, IBlockMover BlockMover)
        NewService()
    {
        var (svc, registry, teleporter, entityMover, blockMover, _) = NewServiceWithApi();
        return (svc, registry, teleporter, entityMover, blockMover);
    }

    private static (TransitService Service, DimensionRegistry Registry, IPlayerTeleporter Teleporter, IEntityMover EntityMover, IBlockMover BlockMover, ICoreServerAPI Sapi)
        NewServiceWithApi()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:target"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var teleporter = Substitute.For<IPlayerTeleporter>();
        var positionResolver = Substitute.For<ITargetPositionResolver>();

        // TransitService resolves twice per transit: a preliminary position (to center the region
        // generation) and, once terrain exists, the final landing position. Distinct values let tests
        // tell the two apart instead of both happening to be (100, 100, 100).
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(1, 1, 1, 10), new BlockPos(100, 100, 100, 10));

        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var entityMover = Substitute.For<IEntityMover>();
        var blockMover = Substitute.For<IBlockMover>();
        blockMover.Move(Arg.Any<BlockPos>(), Arg.Any<BlockPos>()).Returns(true);
        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, entityMover, blockMover, NewDismounter()),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            new InventorySwapper(sapi));

        return (svc, registry, teleporter, entityMover, blockMover, sapi);
    }

    /// <summary>A dismounter substitute that releases the player by default, like the real one for an unmounted player.</summary>
    private static IPlayerDismounter NewDismounter()
    {
        var dismounter = Substitute.For<IPlayerDismounter>();
        dismounter.Dismount(Arg.Any<IServerPlayer>()).Returns(true);
        return dismounter;
    }

    private static (TransitService Service, DimensionRegistry Registry, IPlayerTeleporter Teleporter, IEntityMover EntityMover, IBlockMover BlockMover, IPlayerDismounter Dismounter)
        NewServiceWithDismounter()
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
        var entityMover = Substitute.For<IEntityMover>();
        var blockMover = Substitute.For<IBlockMover>();
        var dismounter = NewDismounter();
        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, entityMover, blockMover, dismounter),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            new InventorySwapper(sapi));

        return (svc, registry, teleporter, entityMover, blockMover, dismounter);
    }
}
