namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using static AtlasFixture.TerrainProbeWorldgen;

/// <summary>
/// Proves TargetPositionResolvers.SameXZSurfaceY against the real engine: TerrainProbeWorldgen
/// (Manifold.Scenarios.FixtureMod) builds seven known columns of real vanilla blocks in the
/// "terrain" dimension, and each scenario here transits a joined player (or, for the lake, an
/// entity) to one of them with SameCoordinates: the unit tests in Manifold.Pure.Tests already
/// pin this resolver down against a fake block accessor; these scenarios pin the same contract
/// down against the actual block types, actual collision data and actual world height. Column
/// X/Y layout is TerrainProbeWorldgen's own constants (see its doc comment for the full picture),
/// shared here at compile time rather than redeclared.
///
/// Every scenario starts the player (or entity) at a distinct, deliberately-tall source Y
/// (<see cref="SourceY"/>) in the overworld, at the target column's X/Z, then transits with no
/// TransitionOptions at all (/atlasfx2 teleport-player-plain, /atlasfx teleport-entity-plain) so
/// the destination's default SpawnBehavior.SameCoordinates picks the landing through the real
/// resolver instead of a fixture-supplied OverridePosition.
///
/// Classes with joined players cannot roll back yet (see README), so this class's host is shared
/// across its scenarios; each uses a distinct player name and a distinct column X, so none of
/// them interfere with each other.
/// </summary>
// rollback-stage2-candidate: every player scenario here hard-refuses stage 1 rollback (joined
// test players are not captured); the entity-only scenario already isolates through RollbackWorld.
[Trait("Category", "E2E")]
public class RealTerrainLandingScenarios : ManifoldScenarioBase
{
    /// <summary>
    /// The overworld Y every scenario starts its player/entity at: tall enough that it can never
    /// collide with the "terrain" dimension's own columns (all of which top out well below this),
    /// so a fallback scenario that keeps the source Y unchanged is unambiguously proven to have
    /// kept THIS Y, not coincided with one of the terrain columns' own blocks.
    /// </summary>
    private const int SourceY = 200;

    [AtlasScenario]
    public async Task Player_Should_LandOnGround_When_ColumnIsOpenGround()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_ground");
        await player.TeleportTo(new BlockPos(OpenGroundX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_ground terrain");

        await LandedExactlyAt(player, terrainId, OpenGroundX, GroundLandingY, ColumnZ);
        AssertNotEmbedded(new BlockPos(OpenGroundX, GroundLandingY, ColumnZ, terrainId));
    }

    [AtlasScenario]
    public async Task Player_Should_LandOnGround_When_ColumnHasTallGrassOnTop()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_grass");
        await player.TeleportTo(new BlockPos(TallGrassX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_grass terrain");

        // Same landing Y as the open-ground column: the grass has no floor to stand on and is
        // walked through, same as air.
        await LandedExactlyAt(player, terrainId, TallGrassX, GroundLandingY, ColumnZ);
        AssertNotEmbedded(new BlockPos(TallGrassX, GroundLandingY, ColumnZ, terrainId));
    }

    [AtlasScenario]
    public async Task Player_Should_KeepSourceY_When_OnlyOpeningIsAOneBlockGap()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_roofgap");
        await player.TeleportTo(new BlockPos(RoofGapX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_roofgap terrain");

        // The column is solid rock except one 1-block gap: room for feet but not head, so it
        // fails the two-block clearance check exactly like solid rock does, and nothing else in
        // the column qualifies either. The source Y comes back unchanged.
        await LandedExactlyAt(player, terrainId, RoofGapX, SourceY, ColumnZ);
    }

    [AtlasScenario]
    public async Task Player_Should_LandOnTheWaterSurface_When_ColumnIsALakeOverABuriedCave()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_lake");
        await player.TeleportTo(new BlockPos(LakeX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_lake terrain");

        // The scan stops at the first liquid found and never reaches the sand bed or the cave
        // underneath it: the lake's own surface is the landing spot, not the drier cave floor
        // several blocks lower.
        await LandedExactlyAt(player, terrainId, LakeX, LakeLandingY, ColumnZ);
        Assert.Equal(
            "game:water-still-7",
            World.BlockAt(new BlockPos(LakeX, LakeLandingY - 1, ColumnZ, terrainId)).Code.ToString());
    }

    [AtlasScenario]
    public async Task Player_Should_KeepSourceY_When_TheOnlyLiquidIsCappedByRock()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_pocket");
        await player.TeleportTo(new BlockPos(WaterPocketX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_pocket terrain");

        // The water pocket has no room above it (capped by rock), so it is never a candidate; the
        // buried cave lower down is never reached either, because the scan already stopped at the
        // capped liquid. Nothing in the column qualifies, so the source Y comes back unchanged.
        await LandedExactlyAt(player, terrainId, WaterPocketX, SourceY, ColumnZ);
    }

    [AtlasScenario]
    public async Task Player_Should_KeepSourceY_When_ColumnIsFullySolid()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_solid");
        await player.TeleportTo(new BlockPos(FullySolidX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_solid terrain");

        await LandedExactlyAt(player, terrainId, FullySolidX, SourceY, ColumnZ);
    }

    [AtlasScenario]
    public async Task Player_Should_KeepSourceY_When_ColumnIsEmpty()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");
        ITestPlayer player = await World.JoinPlayer("atlas_empty");
        await player.TeleportTo(new BlockPos(EmptyColumnX, SourceY, ColumnZ, 0));

        await Ok("/atlasfx2 teleport-player-plain atlas_empty terrain");

        await LandedExactlyAt(player, terrainId, EmptyColumnX, SourceY, ColumnZ);
        AssertNotEmbedded(new BlockPos(EmptyColumnX, SourceY, ColumnZ, terrainId));
    }

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Entity_Should_SurfaceAtTheWater_When_TeleportedToTheLakeColumn()
    {
        int terrainId = await DimensionId("terrain");
        await Ok("/atlasfx pregen terrain");

        BlockPos origin = new(LakeX, SourceY, ColumnZ, 0);
        Entity chicken = World.SpawnEntity("game:chicken-hen", origin);
        await World.Ticks(2);

        CommandResult result = await World.ExecuteCommand($"/atlasfx teleport-entity-plain {chicken.EntityId} terrain");
        Assert.True(result.Ok, result.Message);

        var arrival = new BlockPos(LakeX, LakeLandingY, ColumnZ, terrainId);
        await EntityReaches(chicken, arrival);

        Entity arrived = World.EntitiesIn(arrival.Area(16)).Single(e => e.EntityId == chicken.EntityId);
        Assert.Equal(terrainId, arrived.Pos.Dimension);
        Assert.True(
            Math.Abs(arrived.Pos.Y - LakeLandingY) <= 1,
            $"Expected the entity within 1 block of the water surface (Y {LakeLandingY}), landed at Y {arrived.Pos.Y}.");
        Assert.Equal(
            "game:water-still-7",
            World.BlockAt(new BlockPos(LakeX, LakeLandingY - 1, ColumnZ, terrainId)).Code.ToString());
    }

    /// <summary>Asserts nothing solid occupies <paramref name="pos"/> (same floor test the resolver itself uses).</summary>
    private void AssertNotEmbedded(BlockPos pos)
    {
        Block block = World.BlockAt(pos);
        bool solid = block.SideSolid[BlockFacing.UP.Index] || block.CollisionBoxes is { Length: > 0 };
        Assert.False(solid, $"Expected {pos} (block {block.Code}) to be passable, found it solid.");
    }
}
