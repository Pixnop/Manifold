using Manifold.Api;
using Manifold.Api.Transitions;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TargetPositionResolvers"/>.
/// </summary>
public sealed class TargetPositionResolversTests
{
    [Fact]
    public void SameXZSurfaceY_Should_Find_Solid_Ground_Above_The_Old_Fixed_Scan_Height()
    {
        // A column whose terrain rises above the old hard-coded scan height (160) must still resolve
        // to solid ground: the scan now starts at the world's actual ceiling (MapSizeY - 1).
        const int mapSizeY = 256;
        const int terrainTop = 200; // above the old constant
        var entity = Substitute.For<Entity>();
        entity.Pos.X = 5;
        entity.Pos.Z = 7;
        entity.Pos.Y = 64;

        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);

        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.WorldManager.MapSizeY.Returns(mapSizeY);
        var solidBlock = new Block { BlockId = 1 };
        sapi.World.BlockAccessor
            .GetBlock(Arg.Is<BlockPos>(p => p.Y == terrainTop))
            .Returns(solidBlock);

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.Equal(terrainTop + 1, result.Y);
    }

    [Fact]
    public void SameXZSurfaceY_Should_Keep_Current_Y_When_No_Solid_Ground_Found()
    {
        // A void dimension: every probed block is air (Id 0), so the scan never finds ground and
        // must fall back to the entity's current Y instead of landing at Y 0 or the map ceiling.
        var entity = Substitute.For<Entity>();
        entity.Pos.X = 5;
        entity.Pos.Z = 7;
        entity.Pos.Y = 64;

        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);

        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.WorldManager.MapSizeY.Returns(256);
        sapi.World.BlockAccessor.GetBlock(Arg.Any<BlockPos>()).Returns(new Block { BlockId = 0 });

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.Equal(5, result.X);
        Assert.Equal(64, result.Y);
        Assert.Equal(7, result.Z);
        Assert.Equal(10, result.dimension);
    }
}
