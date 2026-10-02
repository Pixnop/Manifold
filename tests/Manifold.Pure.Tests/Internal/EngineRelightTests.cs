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
    public void IsGateOpen_Should_Follow_The_Overworld_Map_Chunk_Of_The_Column()
    {
        var sapi = NewSapi();
        sapi.WorldManager.GetMapChunk(16, 17).Returns(Substitute.For<IServerMapChunk>());
        var engine = new EngineRelight(sapi);

        Assert.True(engine.IsGateOpen(new BlockPos(520, 6, 550, Dim)));
        Assert.False(engine.IsGateOpen(new BlockPos(550, 6, 550, Dim)));
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
    public void Broadcast_Should_Resend_The_Dimensions_Own_Chunks_One_Chunk_Around_The_Box()
    {
        var sapi = NewSapi();

        new EngineRelight(sapi).Broadcast(new BlockPos(520, 40, 520, Dim), new BlockPos(520, 40, 520, Dim));

        // 3 x 3 x 3 chunks around chunk (16, 1, 16), at the dimension's chunk-Y offset.
        sapi.WorldManager.Received(27).BroadcastChunk(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), true);
        sapi.WorldManager.Received(1).BroadcastChunk(15, (Dim * 1024) + 0, 15, true);
        sapi.WorldManager.Received(1).BroadcastChunk(17, (Dim * 1024) + 2, 17, true);
        sapi.WorldManager.DidNotReceive().BroadcastChunk(Arg.Any<int>(), Arg.Is<int>(cy => cy < Dim * 1024), Arg.Any<int>(), true);
    }

    [Fact]
    public void Broadcast_Should_Clamp_To_The_Map()
    {
        var sapi = NewSapi();

        new EngineRelight(sapi).Broadcast(new BlockPos(0, 0, 0, Dim), new BlockPos(0, 255, 0, Dim));

        // X and Z: chunks 0..1 (nothing below 0); Y: chunks 0..7 (nothing above the 256-block map).
        sapi.WorldManager.Received(2 * 2 * 8).BroadcastChunk(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), true);
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

    private static Block NewBlock(int id, byte light, IBlockAccessor accessor)
    {
        var block = new Block { BlockId = id, LightHsv = new byte[] { 0, 0, light } };
        _ = accessor;
        return block;
    }
}
