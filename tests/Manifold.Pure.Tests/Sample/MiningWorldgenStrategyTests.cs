using System.Collections.Generic;
using Manifold.Api.Worldgen;
using ManifoldSample;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Sample;

/// <summary>
/// Tests for <see cref="MiningWorldgenStrategy"/>'s spawn-room geometry: the fixed spawn point
/// must sit exactly one block above the solid rock floor, matching its own XML doc comment.
/// </summary>
public sealed class MiningWorldgenStrategyTests
{
    [Fact]
    public void SpawnPoint_Should_Sit_One_Block_Above_The_Solid_Rock_Floor()
    {
        var rock = Substitute.For<Block>();
        rock.Id.Returns(1);
        var light = Substitute.For<Block>();
        light.Id.Returns(2);

        var world = Substitute.For<IServerWorldAccessor>();
        world.GetBlock(new AssetLocation("game:rock-granite")).Returns(rock);
        world.GetBlock(new AssetLocation("game:torch-basic-lit-up")).Returns(light);

        var api = Substitute.For<ICoreServerAPI>();
        api.World.Returns(world);

        var initCtx = Substitute.For<IWorldgenInitContext>();
        initCtx.Api.Returns(api);

        var strategy = new MiningWorldgenStrategy(salt: 0);
        strategy.OnInitialize(initCtx);

        // No ore block resolves here, so every non-room column is plain rock: the test does not
        // need to steer the RNG away from the ore roll.
        var placed = new Dictionary<(int X, int Y, int Z), int>();
        var blockAccessor = Substitute.For<IBlockAccessor>();
        blockAccessor
            .When(x => x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>()))
            .Do(call =>
            {
                var pos = call.ArgAt<BlockPos>(1);
                placed[(pos.X, pos.Y, pos.Z)] = call.ArgAt<int>(0);
            });

        var chunkCtx = Substitute.For<IWorldgenChunkContext>();
        chunkCtx.ChunkX.Returns(0);
        chunkCtx.ChunkZ.Returns(0);
        chunkCtx.DimensionId.Returns(7);
        chunkCtx.BlockAccessor.Returns(blockAccessor);
        chunkCtx.Rng.Returns(new LCGRandom(1234));

        strategy.GenerateColumn(chunkCtx);

        int spawnX = MiningWorldgenStrategy.SpawnX;
        int spawnZ = MiningWorldgenStrategy.SpawnZ;
        int spawnY = MiningWorldgenStrategy.SpawnY;

        // One block below the spawn point is solid rock (the floor the player stands on).
        Assert.True(placed.TryGetValue((spawnX, spawnY - 1, spawnZ), out int belowSpawn));
        Assert.Equal(1, belowSpawn);

        // The spawn point itself is the room's lit floor centre, not more rock overhead.
        Assert.True(placed.TryGetValue((spawnX, spawnY, spawnZ), out int atSpawn));
        Assert.Equal(2, atSpawn);
    }
}
