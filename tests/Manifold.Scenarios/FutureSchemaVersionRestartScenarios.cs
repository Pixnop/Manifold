namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Manifold.Internal;
using Xunit;

/// <summary>
/// Proves a world restarted with its sidecar declaring an unrecognized future schema version for
/// one key keeps that key's bytes untouched across a genuine server save and restart, while every
/// other key keeps working normally. The staged fixture (fixtures/future-schema-version) bumps the
/// sidecar's "manifold:genchunks" entry to version 99 (this build supports up to
/// <see cref="GeneratedColumnStore.SchemaVersion"/>) on every save, right after Manifold's own
/// GameWorldSave handler has written the real blob and sidecar for this boot - and snapshots the
/// exact bytes Manifold wrote for "manifold:genchunks" under a recovery key of its own, so this
/// scenario can compare the on-disk bytes AFTER the restart against what was on disk right BEFORE
/// it, the same real save/load round trip a player upgrading (or in this case, briefly
/// downgrading) Manifold actually goes through.
///
/// A separate class from <see cref="DimensionPersistenceScenarios"/> and
/// <see cref="LegacySchemaRestartScenarios"/> (and its own fixture data directory) for the same
/// reason those two are kept apart: Atlas does not guarantee scenario order within a class, and an
/// always-on save hook shared with either of them could make one class's restart see the other's
/// expected on-disk shape depending on which runs first.
/// </summary>
[AtlasDataFiles("fixtures/future-schema-version", TargetPath = "ModConfig")]
[Trait("Category", "E2E")]
public class FutureSchemaVersionRestartScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RestartWorld = true)]
    public async Task Genchunks_Should_StayUntouched_When_ItsSidecarEntryIsAnUnrecognizedFutureVersion()
    {
        byte[]? sidecarRaw = World.Api.WorldManager.SaveGame.GetData("manifold:schema");
        Assert.NotNull(sidecarRaw);
        var sidecar = SchemaSidecar.Load(sidecarRaw);
        Assert.Equal(99, sidecar.GetVersion("manifold:genchunks")); // never downgraded back to 1

        byte[]? onDisk = World.Api.WorldManager.SaveGame.GetData("manifold:genchunks");
        byte[]? beforeRestart = World.Api.WorldManager.SaveGame.GetData("atlasfixture:prevgenchunks");
        Assert.NotNull(beforeRestart);
        Assert.Equal(beforeRestart, onDisk); // this boot never re-wrote the refused key

        byte[]? recovered = World.Api.WorldManager.SaveGame.GetData("manifold:genchunks.unrecognized");
        Assert.Equal(beforeRestart, recovered); // the raw blob was preserved before being refused

        // The rest of the world is unaffected: the manifest itself carries its own sidecar entry
        // (version 1, untouched by the fixture), so dimensions still reload normally.
        Assert.Equal(PrevBootDimensionId("flat"), await DimensionId("flat"));
        CommandResult flatState = await World.ExecuteCommand("/atlasfx state flat");
        Assert.Equal($"Active:{await DimensionId("flat")}", flatState.Message);
    }

    /// <summary>
    /// The id a dimension was published under on the PREVIOUS boot (the fixture snapshots the old
    /// value before overwriting at boot). Non-null here by construction: a RestartWorld scenario
    /// always runs after at least one restart.
    /// </summary>
    private int PrevBootDimensionId(string path)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("atlasfixture:prevdimid:" + path);
        Assert.NotNull(data);
        return BitConverter.ToInt32(data!, 0);
    }
}
