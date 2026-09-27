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
}
