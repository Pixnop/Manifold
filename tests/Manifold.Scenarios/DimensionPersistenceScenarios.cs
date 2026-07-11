namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// What survives a genuine server restart (Atlas 0.7.0 RestartWorld): the dimension manifest.
/// Manifold writes a manifest of persistent dimensions to the savegame on GameWorldSave and
/// reads it back on boot: static dimensions are re-claimed by their owner mod and keep their
/// internal id, runtime persistent dimensions come back Pending under the same id until their
/// owner re-creates them, ephemeral dimensions are dropped entirely. None of this was testable
/// before 0.7.0: FreshWorld throws the savegame away and RollbackWorld never reboots the
/// server, so the load-manifest-on-boot path only ever ran against an empty manifest.
///
/// The pre-restart state is seeded by the FIXTURE at first boot (keeper, ghost), requested via
/// the [AtlasDataFiles]-staged ModConfig below, NOT by a seed scenario: Atlas does not
/// guarantee scenario order within a class (observed under atlas run), so each RestartWorld
/// scenario here is self-sufficient and correct in any order.
/// </summary>
[AtlasDataFiles("fixtures/persistence", TargetPath = "ModConfig")]
[Trait("Category", "E2E")]
public class DimensionPersistenceScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RestartWorld = true)]
    public async Task Manifest_Should_RestorePersistentDimensions_When_ServerRestarts()
    {
        // Static dimensions: seeded Pending from the manifest at boot, then re-claimed by the
        // fixture's RegisterStatic under the SAME internal id. Identity stability across a real
        // save/load round trip is the manifest's core promise: chunk columns and entity
        // positions on disk encode the dimension id.
        Assert.Equal(PrevBootDimensionId("flat"), await DimensionId("flat"));
        Assert.Equal(PrevBootDimensionId("vault"), await DimensionId("vault"));
        CommandResult flatState = await World.ExecuteCommand("/atlasfx state flat");
        Assert.Equal($"Active:{await DimensionId("flat")}", flatState.Message);

        // Static metadata is re-declared at registration, so it is back after the restart.
        CommandResult flatLabel = await World.ExecuteCommand("/atlasfx metadata flat fixture-label");
        Assert.True(flatLabel.Ok, flatLabel.Message);
        Assert.Equal("String:granite-slab", flatLabel.Message);

        // Runtime persistent dimension: nothing re-registered it on the restarted boot, so the
        // manifest entry surfaces as Pending, id preserved. The dimid compared against was
        // written when the fixture created the dimension on the FIRST boot, so the read also
        // proves SaveGame moddata survived the restart.
        int keeperId = await DimensionId("keeper");
        CommandResult keeperState = await World.ExecuteCommand("/atlasfx state keeper");
        Assert.True(keeperState.Ok, keeperState.Message);
        Assert.Equal($"Pending:{keeperId}", keeperState.Message);

        // A Pending dimension refuses transit until its owner re-claims it.
        Entity chicken = World.SpawnEntity("game:chicken-hen", World.Spawn.Offset(2, 1, 2));
        await World.Ticks(2);
        CommandResult refused = await World.ExecuteCommand(
            $"/atlasfx teleport-entity {chicken.EntityId} keeper");
        Assert.False(refused.Ok, "Transit into a Pending dimension was accepted.");
        Assert.Contains("DimensionStateException", refused.Message);

        // The manifest persists identity, not configuration: runtime metadata is gone.
        CommandResult lostLabel = await World.ExecuteCommand("/atlasfx metadata keeper fixture-label");
        Assert.False(lostLabel.Ok, "Runtime metadata survived the restart; the manifest must not persist it.");
        Assert.Equal("missing", lostLabel.Message);

        // Ephemeral dimension: dropped from the manifest, so absent from the registry, even
        // though its stale dimid moddata survived. The manifest decides, not leftovers.
        Assert.NotNull(ReadDimensionId("ghost"));
        CommandResult ghostState = await World.ExecuteCommand("/atlasfx state ghost");
        Assert.False(ghostState.Ok, "An ephemeral dimension survived the restart.");
        Assert.Equal("unregistered", ghostState.Message);
    }

    [AtlasScenario(RestartWorld = true)]
    public async Task Owner_Should_ReclaimPendingDimension_When_RecreatingAfterRestart()
    {
        // After any restart, 'keeper' is back from the manifest as Pending (a Pending entry is
        // re-saved on shutdown, so this holds however many restarts came before).
        int keeperId = await DimensionId("keeper");
        CommandResult pending = await World.ExecuteCommand("/atlasfx state keeper");
        Assert.Equal($"Pending:{keeperId}", pending.Message);

        // The owner re-creates it under the same code: the registry promotes the Pending entry
        // to Active instead of allocating a new id (the documented owner-reclaim path).
        CommandResult reclaimed = await World.ExecuteCommand("/atlasfx create-persistent keeper");
        Assert.True(reclaimed.Ok, reclaimed.Message);
        Assert.Equal($"created {keeperId}", reclaimed.Message);
        CommandResult active = await World.ExecuteCommand("/atlasfx state keeper");
        Assert.Equal($"Active:{keeperId}", active.Message);

        // Re-declared metadata is live again on the promoted dimension.
        CommandResult label = await World.ExecuteCommand("/atlasfx metadata keeper fixture-label");
        Assert.True(label.Ok, label.Message);
        Assert.Equal("String:runtime-persistent", label.Message);

        // And the re-claimed dimension is fully operational: transit in works end to end.
        Entity chicken = World.SpawnEntity("game:chicken-hen", World.Spawn.Offset(4, 1, 4));
        await World.Ticks(2);
        CommandResult transit = await World.ExecuteCommand(
            $"/atlasfx teleport-entity {chicken.EntityId} keeper");
        Assert.True(transit.Ok, transit.Message);
        var arrival = new BlockPos(512, 6, 512, keeperId);
        await World.Until(
            () => World.EntitiesIn(arrival.Area(16)).Any(e => e.EntityId == chicken.EntityId),
            timeoutTicks: 600);
    }

    /// <summary>
    /// The id a dimension was published under on the PREVIOUS boot (the fixture snapshots the
    /// old value before overwriting at boot). Non-null here by construction: a RestartWorld
    /// scenario always runs after at least one restart.
    /// </summary>
    private int PrevBootDimensionId(string path)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("atlasfixture:prevdimid:" + path);
        Assert.NotNull(data);
        return BitConverter.ToInt32(data!, 0);
    }
}
