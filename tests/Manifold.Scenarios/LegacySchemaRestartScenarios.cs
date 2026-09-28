namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// Proves a world that has never run against a Manifold build with schema versioning - no
/// "manifold:schema" sidecar at all - still boots and reloads its dimensions correctly after a
/// genuine server restart. The staged fixture (fixtures/legacy-schema) erases the sidecar on
/// every save, so what actually reaches disk ahead of this scenario's restart, and what the
/// rebooted server reads back, is exactly that legacy shape: every blob key with no sidecar entry,
/// read as schema version 1.
///
/// A separate class from <see cref="DimensionPersistenceScenarios"/> and
/// <see cref="FutureSchemaVersionRestartScenarios"/> (and its own fixture data directory) on
/// purpose: Atlas does not guarantee scenario order within a class, so sharing a host with an
/// always-on stripping hook could make a sibling class's restart see this class's expected shape
/// depending on which one runs first.
/// </summary>
[AtlasDataFiles("fixtures/legacy-schema", TargetPath = "ModConfig")]
[Trait("Category", "E2E")]
public class LegacySchemaRestartScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RestartWorld = true)]
    public async Task Dimensions_Should_StillLoad_When_TheSaveHasNoSchemaSidecarAtAll()
    {
        // The fixture erases the sidecar on every save, so what the pre-restart shutdown actually
        // persisted - and what this boot read back - has no "manifold:schema" key at all.
        byte[]? sidecar = World.Api.WorldManager.SaveGame.GetData("manifold:schema");
        Assert.True(
            sidecar is null or { Length: 0 },
            "The on-disk save carries a schema sidecar; the strip fixture did not run.");

        // Yet every blob still reads correctly, exactly as it did before schema versioning existed:
        // static dimensions are re-claimed under the SAME internal id (the manifest's core promise),
        // and re-visiting one loads its already-generated terrain instead of regenerating it.
        Assert.Equal(PrevBootDimensionId("flat"), await DimensionId("flat"));
        Assert.Equal(PrevBootDimensionId("vault"), await DimensionId("vault"));
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
