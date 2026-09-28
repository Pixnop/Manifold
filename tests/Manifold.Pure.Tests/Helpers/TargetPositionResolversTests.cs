using System.Collections.Generic;
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
    public void SameXZSurfaceY_Should_Land_On_An_Open_Surface()
    {
        var entity = NewEntityAt(5, 64, 7);
        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);
        var sapi = NewSapiWithColumn(256, new Dictionary<int, Block>
        {
            [50] = new Block { BlockId = 1 }, // solid ground; 51 and 52 are air (open sky above)
        });

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.Equal(51, result.Y);
    }

    [Fact]
    public void SameXZSurfaceY_Should_Not_Land_In_A_One_Block_Cave_Ceiling_Gap()
    {
        // The column is solid rock from 61 up to the world ceiling (no sky access here - this is
        // deep underground). 60 is the cave ceiling, 59 a one-block air pocket that must NOT be
        // picked (no room for both feet and head), 58 the cave floor. Below that the cave opens up
        // (40-57 air) down to a real floor at 40, which is the only spot with proper clearance.
        var entity = NewEntityAt(5, 64, 7);
        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);
        var byY = new Dictionary<int, Block>();
        for (int y = 60; y < 256; y++)
        {
            byY[y] = new Block { BlockId = 1 };
        }

        byY[58] = new Block { BlockId = 1 };
        byY[40] = new Block { BlockId = 1 };

        var sapi = NewSapiWithColumn(256, byY);

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.NotEqual(59, result.Y);
        Assert.Equal(41, result.Y);
    }

    [Fact]
    public void SameXZSurfaceY_Should_Land_On_A_Liquid_Surface_Only_When_The_Column_Has_No_Dry_Spot()
    {
        var entity = NewEntityAt(5, 64, 7);
        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);
        var byY = new Dictionary<int, Block>();
        for (int y = 1; y <= 30; y++)
        {
            byY[y] = new Block { BlockId = 2, MatterState = EnumMatterState.Liquid };
        }

        var sapi = NewSapiWithColumn(256, byY);

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.Equal(31, result.Y);
    }

    [Fact]
    public void SameXZSurfaceY_Should_Prefer_Dry_Ground_Under_A_Liquid_Surface()
    {
        var entity = NewEntityAt(5, 64, 7);
        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);
        var sapi = NewSapiWithColumn(256, new Dictionary<int, Block>
        {
            [30] = new Block { BlockId = 2, MatterState = EnumMatterState.Liquid },
            [20] = new Block { BlockId = 1 }, // dry lake bed, open above it
        });

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.Equal(21, result.Y);
    }

    [Fact]
    public void SameXZSurfaceY_Should_Keep_Current_Y_When_The_Column_Is_Fully_Solid()
    {
        var entity = NewEntityAt(5, 64, 7);
        var target = Substitute.For<IDimension>();
        target.InternalId.Returns(10);
        var byY = new Dictionary<int, Block>();
        for (int y = 1; y < 256; y++)
        {
            byY[y] = new Block { BlockId = 1 };
        }

        var sapi = NewSapiWithColumn(256, byY);

        var result = TargetPositionResolvers.SameXZSurfaceY.Resolve(entity, target, sapi);

        Assert.Equal(64, result.Y);
    }

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

    /// <summary>
    /// Builds a fake block accessor for a single X/Z column: <paramref name="byY"/> maps a Y level
    /// to its block; any Y not present is air (block id 0).
    /// </summary>
    private static ICoreServerAPI NewSapiWithColumn(int mapSizeY, Dictionary<int, Block> byY)
    {
        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.WorldManager.MapSizeY.Returns(mapSizeY);
        sapi.World.BlockAccessor.GetBlock(Arg.Any<BlockPos>()).Returns(call =>
        {
            var pos = call.Arg<BlockPos>();
            return byY.TryGetValue(pos.Y, out var block) ? block : new Block { BlockId = 0 };
        });
        return sapi;
    }

    private static Entity NewEntityAt(int x, int y, int z)
    {
        var entity = Substitute.For<Entity>();
        entity.Pos.X = x;
        entity.Pos.Y = y;
        entity.Pos.Z = z;
        return entity;
    }
}
