using System;
using System.Collections.Generic;
using Manifold.Internal;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class EngineRelightTests
{
    private const int Dim = 5;
    private const int LampId = 2;

    [Fact]
    public void FullRelight_Should_Never_Use_The_Engines_Resend()
    {
        var sapi = NewSapi();

        bool ok = new EngineRelight(sapi).FullRelight(new BlockPos(0, 0, 0, Dim), new BlockPos(31, 31, 31, Dim));

        Assert.True(ok);
        sapi.WorldManager.Received(1).FullRelight(Arg.Any<BlockPos>(), Arg.Any<BlockPos>(), false);
    }

    [Fact]
    public void FullRelight_Should_Mark_The_Dimensions_Relit_Chunks_Modified()
    {
        var sapi = NewSapi();
        IServerChunk inside = Substitute.For<IServerChunk>();
        IServerChunk overworld = Substitute.For<IServerChunk>();
        sapi.WorldManager.GetChunk(16, Dim * 1024, 16).Returns(inside);
        sapi.WorldManager.GetChunk(16, 0, 16).Returns(overworld);

        new EngineRelight(sapi).FullRelight(new BlockPos(520, 6, 520, Dim), new BlockPos(520, 6, 520, Dim));

        // Otherwise the recomputed sunlight is not saved; and never the overworld's chunk at the same X/Y/Z.
        inside.Received(1).MarkModified();
        overworld.DidNotReceive().MarkModified();
    }

    [Fact]
    public void FullRelight_Should_Return_False_And_Log_A_Warning_When_The_Engine_Throws()
    {
        var sapi = NewSapi();
        sapi.WorldManager
            .When(w => w.FullRelight(Arg.Any<BlockPos>(), Arg.Any<BlockPos>(), Arg.Any<bool>()))
            .Do(_ => throw new InvalidOperationException("chunk not loaded"));

        // Best-effort: the caller (the /manifold relight command) must learn about the failure
        // through the return value, not an escaping exception.
        bool ok = new EngineRelight(sapi).FullRelight(new BlockPos(0, 0, 0, Dim), new BlockPos(31, 31, 31, Dim));

        Assert.False(ok);
        sapi.Logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void FindLightSources_Should_Return_Tracked_Lights_And_Untracked_Emitting_Blocks()
    {
        var sapi = NewSapi();
        IServerChunk chunk = Substitute.For<IServerChunk>();
        chunk.Empty.Returns(false);

        // Index of local (x 3, y 2, z 1): tracked by the engine, block type does not emit by itself.
        int tracked = (((2 * 32) + 1) * 32) + 3;

        // Index of local (x 4, y 0, z 0): an emitting block the engine does not track yet.
        const int untracked = 4;
        chunk.LightPositions.Returns(new HashSet<int> { tracked });
        chunk.Data.GetBlockId(untracked, BlockLayersAccess.Solid).Returns(LampId);
        chunk.Data.When(d => d.FuzzyListBlockIds(Arg.Any<List<int>>())).Do(c => c.Arg<List<int>>().Add(LampId));

        // Chunk (16, 0, 16) of the dimension; every other chunk around is absent or empty.
        sapi.WorldManager.GetChunk(16, Dim * 1024, 16).Returns(chunk);
        IServerChunk empty = Substitute.For<IServerChunk>();
        empty.Empty.Returns(true);
        sapi.WorldManager.GetChunk(17, Dim * 1024, 16).Returns(empty);

        var found = new EngineRelight(sapi).FindLightSources(
            new BlockPos(520, 6, 520, Dim), new BlockPos(520, 6, 520, Dim));

        Assert.Equal(2, found.Count);
        Assert.Contains(new BlockPos(515, 2, 513, Dim), found);
        Assert.Contains(new BlockPos(516, 0, 512, Dim), found);
        _ = empty.DidNotReceive().Data;
    }

    [Fact]
    public void FindLightSources_Should_Not_Read_Solid_Cells_When_The_Palette_Has_No_Emitter_But_Still_Find_Fluid_Lights()
    {
        var sapi = NewSapi();
        IServerChunk chunk = Substitute.For<IServerChunk>();
        chunk.Empty.Returns(false);
        chunk.LightPositions.Returns(new HashSet<int>());
        chunk.Data.When(d => d.FuzzyListBlockIds(Arg.Any<List<int>>())).Do(c => c.Arg<List<int>>().Add(1));
        chunk.Data.GetFluid(4).Returns(LampId);
        sapi.WorldManager.GetChunk(16, Dim * 1024, 16).Returns(chunk);

        var found = new EngineRelight(sapi).FindLightSources(
            new BlockPos(520, 6, 520, Dim), new BlockPos(520, 6, 520, Dim));

        Assert.Equal(new BlockPos(516, 0, 512, Dim), Assert.Single(found));
        chunk.Data.DidNotReceive().GetBlockId(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void FindLightSources_Should_Skip_A_Chunk_That_Cannot_Be_Read_And_Log_It()
    {
        var sapi = NewSapi();
        IServerChunk broken = Substitute.For<IServerChunk>();
        broken.Empty.Returns(false);
        broken.When(c => c.Unpack()).Do(_ => throw new InvalidOperationException("corrupt"));
        sapi.WorldManager.GetChunk(16, Dim * 1024, 16).Returns(broken);

        var found = new EngineRelight(sapi).FindLightSources(
            new BlockPos(520, 6, 520, Dim), new BlockPos(520, 6, 520, Dim));

        Assert.Empty(found);
        sapi.Logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void FindLightSources_Should_Still_Scan_Blocks_When_The_Tracked_Set_Is_Being_Changed()
    {
        // The engine's relight thread mutates LightPositions while it works; copying it can throw.
        var sapi = NewSapi();
        IServerChunk chunk = Substitute.For<IServerChunk>();
        chunk.Empty.Returns(false);
        chunk.LightPositions.Returns(_ => throw new InvalidOperationException("Collection was modified"));
        chunk.Data.GetBlockId(4, BlockLayersAccess.Solid).Returns(LampId);
        chunk.Data.When(d => d.FuzzyListBlockIds(Arg.Any<List<int>>())).Do(c => c.Arg<List<int>>().Add(LampId));
        sapi.WorldManager.GetChunk(16, Dim * 1024, 16).Returns(chunk);

        var found = new EngineRelight(sapi).FindLightSources(
            new BlockPos(520, 6, 520, Dim), new BlockPos(520, 6, 520, Dim));

        Assert.Equal(new BlockPos(516, 0, 512, Dim), Assert.Single(found));
    }

    [Fact]
    public void IsGateOpen_Should_Follow_The_Overworld_Map_Chunk_Of_The_Column()
    {
        var sapi = NewSapi();
        sapi.WorldManager.GetMapChunk(16, 17).Returns(Substitute.For<IServerMapChunk>());
        var engine = new EngineRelight(sapi);

        Assert.True(engine.IsGateOpen(16, 17));
        Assert.False(engine.IsGateOpen(17, 17));
    }

    [Fact]
    public void QueueBlockLight_Should_Exchange_An_Emitting_Block_For_Itself_Through_A_Relighting_Accessor()
    {
        var sapi = NewSapi();
        var pos = new BlockPos(520, 6, 520, Dim);
        Block lamp = NewBlock(LampId, 21, sapi.World.BlockAccessor);
        sapi.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Returns(lamp);
        IBlockAccessor relighting = Substitute.For<IBlockAccessor>();
        sapi.World.GetBlockAccessor(false, true, false).Returns(relighting);

        Assert.True(new EngineRelight(sapi).QueueBlockLight(pos));

        relighting.Received(1).ExchangeBlock(LampId, pos);
    }

    [Fact]
    public void QueueBlockLight_Should_Find_A_Light_In_The_Fluid_Layer()
    {
        var sapi = NewSapi();
        var pos = new BlockPos(520, 6, 520, Dim);
        Block air = NewBlock(0, 0, sapi.World.BlockAccessor);
        Block lava = NewBlock(LampId, 21, sapi.World.BlockAccessor);
        sapi.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Returns(air);
        sapi.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid).Returns(lava);
        IBlockAccessor relighting = Substitute.For<IBlockAccessor>();
        sapi.World.GetBlockAccessor(false, true, false).Returns(relighting);

        Assert.True(new EngineRelight(sapi).QueueBlockLight(pos));

        relighting.Received(1).ExchangeBlock(LampId, pos);
    }

    [Fact]
    public void QueueBlockLight_Should_Write_Nothing_When_No_Block_Emits_Light_There()
    {
        var sapi = NewSapi();
        var pos = new BlockPos(520, 6, 520, Dim);
        Block air = NewBlock(0, 0, sapi.World.BlockAccessor);
        sapi.World.BlockAccessor.GetBlock(pos, Arg.Any<int>()).Returns(air);

        Assert.False(new EngineRelight(sapi).QueueBlockLight(pos));

        sapi.World.DidNotReceive().GetBlockAccessor(Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public void EmitsLight_Should_Tell_A_Light_Source_From_Any_Other_Block()
    {
        var sapi = NewSapi();
        var lampPos = new BlockPos(520, 6, 520, Dim);
        var rockPos = new BlockPos(521, 6, 520, Dim);
        IBlockAccessor accessor = sapi.World.BlockAccessor;
        Block lamp = NewBlock(LampId, 21, accessor);
        Block rock = NewBlock(1, 0, accessor);
        accessor.GetBlock(lampPos, BlockLayersAccess.Solid).Returns(lamp);
        accessor.GetBlock(rockPos, Arg.Any<int>()).Returns(rock);
        var engine = new EngineRelight(sapi);

        Assert.True(engine.EmitsLight(lampPos));
        Assert.False(engine.EmitsLight(rockPos));
    }

    [Fact]
    public void IsLit_Should_Read_Block_Light_Only()
    {
        var sapi = NewSapi();
        var lit = new BlockPos(1, 1, 1, Dim);
        sapi.World.BlockAccessor.GetLightLevel(lit, EnumLightLevelType.OnlyBlockLight).Returns(21);
        var engine = new EngineRelight(sapi);

        Assert.True(engine.IsLit(lit));
        Assert.False(engine.IsLit(new BlockPos(2, 1, 1, Dim)));
    }

    [Fact]
    public void CollectAffectedChunks_Should_List_Loaded_Chunks_One_Chunk_Around_The_Box_Clamped_To_The_Map()
    {
        var sapi = NewSapi();
        IServerChunk loaded = Substitute.For<IServerChunk>();
        sapi.WorldManager.GetChunk(Arg.Any<int>(), Arg.Is<int>(cy => cy >= Dim * 1024 && cy < (Dim * 1024) + 8), Arg.Any<int>())
            .Returns(loaded);
        var chunks = new HashSet<(int Cx, int Cy, int Cz)>();

        new EngineRelight(sapi).CollectAffectedChunks(new BlockPos(0, 0, 0, Dim), new BlockPos(0, 32767, 0, Dim), chunks);

        // X and Z: chunks 0..1 (nothing below 0); Y: chunks 0..7 (nothing above the 256-block map).
        Assert.Equal(2 * 2 * 8, chunks.Count);
        Assert.Contains((1, 7, 1), chunks);
    }

    [Fact]
    public void CollectAffectedChunks_Should_Leave_Out_Chunks_That_Are_Not_Loaded()
    {
        var sapi = NewSapi();
        sapi.WorldManager.GetChunk(16, (Dim * 1024) + 1, 16).Returns(Substitute.For<IServerChunk>());
        var chunks = new HashSet<(int Cx, int Cy, int Cz)>();

        new EngineRelight(sapi).CollectAffectedChunks(new BlockPos(520, 40, 520, Dim), new BlockPos(520, 40, 520, Dim), chunks);

        Assert.Equal((16, 1, 16), Assert.Single(chunks));
    }

    [Fact]
    public void Resend_Should_Send_Loaded_Chunks_Only_To_Players_In_That_Dimension()
    {
        var sapi = NewSapi();
        IServerPlayer inside = PlayerIn(Dim);
        IServerPlayer elsewhere = PlayerIn(0);
        sapi.World.AllOnlinePlayers.Returns(new IPlayer[] { inside, elsewhere });
        sapi.WorldManager.GetChunk(16, (Dim * 1024) + 1, 16).Returns(Substitute.For<IServerChunk>());

        new EngineRelight(sapi).Resend(Dim, new[] { (16, 1, 16), (17, 1, 16) });

        // The dimension's own chunk index, the loaded chunk only, and never a player standing at
        // the same X/Z in another dimension.
        sapi.WorldManager.Received(1).SendChunk(16, (Dim * 1024) + 1, 16, inside, true);
        sapi.WorldManager.Received(1).SendChunk(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IServerPlayer>(), Arg.Any<bool>());
        sapi.WorldManager.DidNotReceive().BroadcastChunk(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public void Warn_Should_Log_A_Warning()
    {
        var sapi = NewSapi();

        new EngineRelight(sapi).Warn("pending relight dropped");

        sapi.Logger.Received(1).Warning("pending relight dropped");
    }

    private static ICoreServerAPI NewSapi()
    {
        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.WorldManager.MapSizeX.Returns(1024000);
        sapi.WorldManager.MapSizeY.Returns(256);
        sapi.WorldManager.MapSizeZ.Returns(1024000);
        sapi.WorldManager.GetChunk(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns((IServerChunk?)null);
        sapi.WorldManager.GetMapChunk(Arg.Any<int>(), Arg.Any<int>()).Returns((IServerMapChunk?)null);
        IBlockAccessor accessor = sapi.World.BlockAccessor;
        sapi.World.Blocks.Returns(new List<Block> { NewBlock(0, 0, accessor), NewBlock(1, 0, accessor), NewBlock(LampId, 21, accessor) });
        return sapi;
    }

    private static IServerPlayer PlayerIn(int dimension)
    {
        // Entity.Pos is not virtual: the substitute carries a real EntityPos whose Dimension is set directly.
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = dimension;
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(entity);
        return player;
    }

    private static Block NewBlock(int id, byte light, IBlockAccessor accessor)
    {
        var block = new Block { BlockId = id, LightHsv = new byte[] { 0, 0, light } };
        _ = accessor;
        return block;
    }
}
