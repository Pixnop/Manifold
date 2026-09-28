namespace AtlasFixture;

using System;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

/// <summary>
/// Builds seven known columns of real vanilla blocks for proving
/// <c>TargetPositionResolvers.SameXZSurfaceY</c> against the actual engine instead of a fake
/// block accessor (RealTerrainLandingScenarios). Every column is a single block wide, shares
/// Z = <see cref="ColumnZ"/>, and sits inside the one chunk the "terrain" dimension pregenerates
/// (<c>WithGenerationRadius(0)</c>, centered on the fixture's shared FixedSpawn X/Z, 512,512).
/// Column layout (world X, Z = ColumnZ):
///
///  X 512 <see cref="OpenGroundX"/>    soil at Y 10, open sky above.
///                                     Expected landing: Y 11, standing on the soil.
///  X 516 <see cref="TallGrassX"/>     soil at Y 10, tallgrass at Y 11, open sky above.
///                                     Expected landing: Y 11 too (the grass is walked through,
///                                     same floor as the open-ground column).
///  X 520 <see cref="RoofGapX"/>       rock from Y 1 to the world ceiling, except for a single
///                                     1-block air gap at Y 11 (the column's only opening).
///                                     Expected landing: the source Y, unchanged (the gap has room
///                                     for feet but not head, so it fails the two-block clearance
///                                     check and is skipped, same as solid rock; nothing else in
///                                     the column qualifies).
///  X 524 <see cref="LakeX"/>          rock base (Y 1-8, with a buried 2-tall cave at Y 5-6 that
///                                     must never be reached), sand at Y 9, water source blocks
///                                     Y 10-15, open sky above. Expected landing: Y 16, on the
///                                     water's surface (the scan stops at the first liquid found
///                                     and never searches the seabed below it).
///  X 528 <see cref="WaterPocketX"/>   a single water block at Y 10, capped directly above by
///                                     rock, with rock filling the rest of the column from Y 1 to
///                                     the world ceiling, including a second buried 2-tall cave
///                                     at Y 5-6 that must never be reached either. Expected
///                                     landing: the source Y, unchanged (the water has no room
///                                     above it, and nothing else in the column qualifies).
///  X 532 <see cref="FullySolidX"/>    rock from Y 1 to the world ceiling, no gap anywhere.
///                                     Expected landing: the source Y, unchanged.
///  X 536 <see cref="EmptyColumnX"/>   nothing generated (air throughout).
///                                     Expected landing: the source Y, unchanged.
/// </summary>
internal sealed class TerrainProbeWorldgen : IWorldgenStrategy
{
    public const int ColumnZ = 512;
    public const int OpenGroundX = 512;
    public const int TallGrassX = 516;
    public const int RoofGapX = 520;
    public const int LakeX = 524;
    public const int WaterPocketX = 528;
    public const int FullySolidX = 532;
    public const int EmptyColumnX = 536;

    /// <summary>Expected landing Y for <see cref="OpenGroundX"/> and <see cref="TallGrassX"/>.</summary>
    public const int GroundLandingY = GroundTopY + 1;

    /// <summary>Expected landing Y for <see cref="LakeX"/>: the water's own surface.</summary>
    public const int LakeLandingY = LakeWaterTopY + 1;

    private const int GroundTopY = 10;

    /// <summary>The one air block in the <see cref="RoofGapX"/> column: too shallow to land in.</summary>
    private const int RoofGapY = 11;

    private const int CaveFloorY = 4;
    private const int LakeRockTopY = 8;
    private const int LakeSandY = 9;
    private const int LakeWaterBottomY = 10;
    private const int LakeWaterTopY = 15;

    private const int PocketWaterY = 10;

    private int _graniteId;
    private int _soilId;
    private int _tallgrassId;
    private int _sandId;
    private int _waterId;
    private int _top;

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        _graniteId = ctx.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;
        _soilId = ctx.Api.World.GetBlock(new AssetLocation("game", "soil-medium-none"))!.BlockId;
        _tallgrassId = ctx.Api.World.GetBlock(new AssetLocation("game", "tallgrass-medium-free"))!.BlockId;
        _sandId = ctx.Api.World.GetBlock(new AssetLocation("game", "sand-granite"))!.BlockId;
        _waterId = ctx.Api.World.GetBlock(new AssetLocation("game", "water-still-7"))!.BlockId;

        // Mirrors TargetPositionResolvers.SameXZSurfaceY's own scan ceiling: the fully-solid
        // column must reach the real world top, or an unfilled gap above it would be picked as a
        // (wrong) landing spot instead of proving the "no valid spot" fallback.
        _top = ctx.Api.WorldManager.MapSizeY - 1;
    }

    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        FillColumn(ctx, OpenGroundX, OpenGround);
        FillColumn(ctx, TallGrassX, TallGrass);
        FillColumn(ctx, RoofGapX, RoofGap);
        FillColumn(ctx, LakeX, Lake);
        FillColumn(ctx, WaterPocketX, WaterPocket);
        FillColumn(ctx, FullySolidX, FullySolid);

        // EmptyColumnX: nothing to place, the column stays air.
    }

    private void FillColumn(IWorldgenChunkContext ctx, int worldX, Action<IWorldgenChunkContext, int, int> fill)
    {
        int localX = worldX - (ctx.ChunkX * 32);
        int localZ = ColumnZ - (ctx.ChunkZ * 32);
        if (localX is < 0 or >= 32 || localZ is < 0 or >= 32)
        {
            // Outside this chunk: with WithGenerationRadius(0) centered on 512,512 only the
            // chunk containing every column above is ever generated, but guard anyway rather
            // than assume it.
            return;
        }

        fill(ctx, localX, localZ);
    }

    private void OpenGround(IWorldgenChunkContext ctx, int lx, int lz) =>
        Set(ctx, lx, GroundTopY, lz, _soilId);

    private void TallGrass(IWorldgenChunkContext ctx, int lx, int lz)
    {
        Set(ctx, lx, GroundTopY, lz, _soilId);
        Set(ctx, lx, GroundTopY + 1, lz, _tallgrassId);
    }

    private void RoofGap(IWorldgenChunkContext ctx, int lx, int lz)
    {
        for (int y = 1; y <= _top; y++)
        {
            if (y == RoofGapY)
            {
                continue; // The column's only opening: 1 block, not enough for feet and head.
            }

            Set(ctx, lx, y, lz, _graniteId);
        }
    }

    private void Lake(IWorldgenChunkContext ctx, int lx, int lz)
    {
        for (int y = 1; y <= LakeRockTopY; y++)
        {
            if (y is CaveFloorY + 1 or CaveFloorY + 2)
            {
                continue; // The buried cave's 2-block-tall interior: stays air, never reached.
            }

            Set(ctx, lx, y, lz, _graniteId);
        }

        Set(ctx, lx, LakeSandY, lz, _sandId);
        for (int y = LakeWaterBottomY; y <= LakeWaterTopY; y++)
        {
            Set(ctx, lx, y, lz, _waterId);
        }
    }

    private void WaterPocket(IWorldgenChunkContext ctx, int lx, int lz)
    {
        for (int y = 1; y <= _top; y++)
        {
            if (y == PocketWaterY || y is CaveFloorY + 1 or CaveFloorY + 2)
            {
                continue; // The water pocket itself, and the second buried cave's interior below it.
            }

            Set(ctx, lx, y, lz, _graniteId);
        }

        Set(ctx, lx, PocketWaterY, lz, _waterId);
    }

    private void FullySolid(IWorldgenChunkContext ctx, int lx, int lz)
    {
        for (int y = 1; y <= _top; y++)
        {
            Set(ctx, lx, y, lz, _graniteId);
        }
    }

    private static void Set(IWorldgenChunkContext ctx, int lx, int y, int lz, int blockId) =>
        ctx.BlockAccessor.SetBlock(blockId, new BlockPos((ctx.ChunkX * 32) + lx, y, (ctx.ChunkZ * 32) + lz, ctx.DimensionId));
}
