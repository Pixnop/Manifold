namespace Manifold.Scenarios.CompatFixturesUpgrade;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Builds the world `atlas fixture` harvests as fixtures/upgrade-from-0.5.1.vcdbs (see
/// README.md): a world played entirely with the published Manifold 0.5.1 release and the
/// compat fixture compiled against the 0.5.1 API. Manifold.Scenarios.Compat's
/// UpgradeVerifyScenarios then boots that exact save with the dev build and asserts what
/// survived the version change.
/// </summary>
[Trait("Category", "E2E")]
[AtlasWorld(
    Mods = new[] { "atlas-mods/manifold051.zip", "atlas-mods/CompatFixture" },
    ExcludeAssemblyMods = true)]
public class UpgradeFixtureBuilderScenarios : CompatFixtureScenarioBase
{
    [AtlasScenario]
    public async Task BuildTheUpgradeFixture()
    {
        int dimId = await CompatDimensionId();

        ITestPlayer player = await World.JoinPlayer("compat051");
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Player.BroadcastPlayerData(true);
        BlockPos start = World.Spawn.Offset(40, 0, 40);
        await player.TeleportTo(start);

        // An overworld-only marker: stashed into the "overworld" category of the per-dimension
        // inventory profile the moment the player enters below, so it never sits in the live
        // hotbar at harvest time, only inside Manifold's own persisted moddata. The verifier's
        // "leave" call has to actually read that persisted store (written by THIS build) back
        // correctly to make it reappear; the engine's own generic save/load of the live hotbar
        // cannot produce that on its own (see DowngradeVerifyScenarios/UpgradeVerifyScenarios).
        await player.GiveItem("game:stick", 5);

        // First visit: LastVisited has nothing recorded, so entry falls back to the pre-transit
        // X/Z; entry also forces Creative, saving Survival to restore later.
        await Ok("/manicompat enter compat051");
        await LandedAt(player, dimId, start.X, start.Z);
        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);

        // The block a player placed: distinct from the slab worldgen's granite, so the verifier
        // can tell "still there" apart from "terrain regenerated over it".
        var placedAt = new BlockPos(player.Position.X, player.Position.Y + 1, player.Position.Z, dimId);
        World.SetBlock("game:chest-east", placedAt);
        await World.Ticks(2);
        PublishPosition("placed", placedAt);

        // A separate-inventory profile: an item that only exists in this dimension's own set.
        await player.GiveItem("game:gear-rusty", 3);

        // Move, then leave: records LastVisited at the new spot and restores the saved Survival
        // mode (consuming the pre-forced-mode blob written on entry above).
        var lastVisited = new BlockPos(player.Position.X + 3, player.Position.Y, player.Position.Z + 3, dimId);
        await player.TeleportTo(lastVisited);
        await Ok("/manicompat leave compat051");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);
        PublishPosition("lastvisited", lastVisited);

        // Re-enter: lands at the recorded LastVisited spot (proven pre-transition, so a
        // regression here would fail this builder scenario itself rather than only surfacing in
        // the verifier) and forces Creative again. The player stays inside for the harvest, so a
        // fresh pre-forced-mode save (Survival) is the one on disk for the dev build to restore.
        await Ok("/manicompat enter compat051");
        await LandedAt(player, dimId, lastVisited.X, lastVisited.Z);
        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);

        // A second player, distinct from the one that stays inside "compat" for the harvest:
        // enters from its own known spot, moves, and leaves, ending up in the OVERWORLD at
        // harvest time, with a "compat" LastVisited entry that exists only in THIS build's
        // position store. The verifier joins the same player, teleports it elsewhere in the
        // overworld, and enters: landing back at the recorded spot proves the entry was read
        // from the persisted store across the version change (see UpgradeVerifyScenarios's own
        // primary-player LastVisited check, which the verifier's own "leave" call would
        // otherwise silently overwrite before the verifier ever reads it back).
        ITestPlayer second = await World.JoinPlayer("compat051-second");
        BlockPos secondStart = World.Spawn.Offset(-60, 0, 60);
        await second.TeleportTo(secondStart);
        await Ok("/manicompat enter compat051-second");
        await LandedAt(second, dimId, secondStart.X, secondStart.Z);

        var secondLastVisited = new BlockPos(second.Position.X - 5, second.Position.Y, second.Position.Z - 5, dimId);
        await second.TeleportTo(secondLastVisited);
        await Ok("/manicompat leave compat051-second");
        await World.Until(() => second.Position.dimension == 0, timeoutTicks: 600);
        PublishPosition("secondlastvisited", secondLastVisited);
    }
}
