namespace Manifold.Scenarios.CompatDowngrade;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Loads fixtures/downgrade-from-dev.vcdbs, a world played with THIS repo's dev build, which
/// also writes the "manifold:schema" sidecar (see
/// tests/Manifold.Scenarios.CompatFixtures/DowngradeFixtureBuilderScenarios), with the published
/// Manifold 0.5.1 release: the "downgrade" direction. 0.5.1 has never heard of the sidecar; this
/// asserts it does not stop the world from loading or regenerate terrain over the placed block.
/// </summary>
[Trait("Category", "E2E")]
[AtlasWorld(
    Mods = new[] { "atlas-mods/manifold051.zip", "atlas-mods/CompatFixture" },
    ExcludeAssemblyMods = true,
    SaveFile = "fixtures/downgrade-from-dev.vcdbs")]
public class DowngradeVerifyScenarios : CompatVerifyScenarioBase
{
    [AtlasScenario]
    public async Task The051Release_Should_KeepDimensionIdTerrainPositionsInventoryAndForcedMode_When_OpeningADevWorld()
    {
        // The sidecar a build this old never heard of is still exactly where the dev build left
        // it: 0.5.1 neither strips it nor fails to boot over it.
        byte[]? sidecar = World.Api.WorldManager.SaveGame.GetData("manifold:schema");
        Assert.True(sidecar is { Length: > 0 }, "The manifold:schema sidecar the dev build wrote did not survive opening with 0.5.1.");

        // Same persistent dimension, same internal id: the manifest round-tripped across the
        // version change, not just across an ordinary restart.
        int dimId = await CompatDimensionId();
        Assert.Equal(PreviousDimensionId(), dimId);

        // Reconnect the SAME player (Atlas derives a stable id from the player name), restored
        // exactly where the dev side left them: inside "compat", forced Creative, holding the
        // item their separate-inventory profile gave them there. This also loads the chunk they
        // are standing in, a precondition for the block reads below: a fresh boot loads chunks
        // on demand, not eagerly for every previously-generated column.
        ITestPlayer player = await World.JoinPlayer("compatdev");
        await World.Until(() => player.Position.dimension == dimId, timeoutTicks: 600);
        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);
        Assert.True(HotbarCount(player, "game:gear-rusty") >= 3, "The separate-inventory item did not survive the version change.");

        // The block a player placed: still there, not overwritten by a regeneration. This is the
        // core downgrade risk named in the brief: an older build regenerating terrain it does
        // not recognize instead of loading what is already on disk.
        BlockPos placedAt = ReadPosition("placed", dimId);
        await World.Until(() => World.BlockAt(placedAt).Code?.ToString() != "game:air", timeoutTicks: 200);
        Assert.Equal("game:chest-east", World.BlockAt(placedAt).Code?.ToString());

        // Generated terrain intact around it, in the same column the dev-side player actually
        // stood in.
        var slabBelow = new BlockPos(placedAt.X, 2, placedAt.Z, dimId);
        Assert.Equal("game:rock-granite", World.BlockAt(slabBelow).Code?.ToString());

        // Saved pre-forced game mode: leaving restores Survival, the mode the dev side saved
        // right before the world was harvested.
        await Ok("/manicompat leave compatdev");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);

        // Separate-inventory profile, the real cross-version read: this "leave" transit swaps in
        // whatever the "overworld" category holds, and that snapshot was written by the dev side
        // (see DowngradeFixtureBuilderScenarios) into Manifold's persisted moddata, never into
        // the live hotbar the engine's own save/load would carry over on its own. Landing back on
        // the marker item here means THIS build (0.5.1) correctly deserialized THAT build's store.
        await World.Until(() => HotbarCount(player, "game:stick") == 5, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:gear-rusty"));

        // Last-visited position: moving somewhere else first, THEN re-entering, still lands back
        // at the coordinates the dev side recorded (not the current position, and not the
        // dimension's own spawn): proof the memory is read from the persisted profile, not from
        // whatever happens to be the player's position right now.
        BlockPos lastVisited = ReadPosition("lastvisited", dimId);
        await player.TeleportTo(World.Spawn.Offset(500, 0, 500));
        await Ok("/manicompat enter compatdev");
        await LandedAt(player, dimId, lastVisited.X, lastVisited.Z);

        // Separate-inventory profile, the symmetric swap back: the "compat" category the leave
        // step above just wrote (this build's own gear-rusty snapshot) round-trips cleanly.
        await World.Until(() => HotbarCount(player, "game:gear-rusty") == 3, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:stick"));

        // A second player's last-visited position: unlike the primary player above, this one
        // never transits during THIS verifier run before being checked, so nothing here can have
        // overwritten its "compat" position-store entry: the entry read below was written
        // entirely by the dev side (see DowngradeFixtureBuilderScenarios) and never touched since.
        BlockPos secondLastVisited = ReadPosition("secondlastvisited", dimId);
        ITestPlayer second = await World.JoinPlayer("compatdev-second");
        await second.TeleportTo(World.Spawn.Offset(-500, 0, -500));
        await Ok("/manicompat enter compatdev-second");
        await LandedAt(second, dimId, secondLastVisited.X, secondLastVisited.Z);
    }
}
