namespace AtlasFixture;

using Vintagestory.API.Datastructures;

/// <summary>
/// Fixture surface for the schema-sidecar restart scenarios. Talks to Manifold's savegame keys
/// directly, the way a migration tool or a hostile save file would: this assembly has no access
/// to Manifold's internals (see AtlasFixtureModSystem.Coverage's manifest-envelope comment for
/// the same reasoning), so the sidecar's documented encoding (a TreeAttribute mapping blob key to
/// version) is reproduced here rather than referenced.
/// </summary>
public sealed partial class AtlasFixtureModSystem
{
    /// <summary>Savegame key the fixture snapshots the pre-restart "manifold:genchunks" bytes under, for FutureSchemaVersionRestartScenarios to compare against.</summary>
    internal const string PrevGenchunksKey = "atlasfixture:prevgenchunks";

    private const string SchemaSidecarKey = "manifold:schema";
    private const string GeneratedColumnsKey = "manifold:genchunks";

    private void StartSchemaFixtures(AtlasFixtureConfig config)
    {
        if (config.StripSchemaSidecar)
        {
            // Registered after Manifold's own StartServerSide (ExecuteOrder 0.05 < this fixture's
            // 0.5), so this handler runs AFTER Manifold has written the sidecar for this save,
            // overwriting it back to "absent" before the bytes ever reach disk.
            _sapi.Event.GameWorldSave += StripSchemaSidecarOnSave;
        }

        if (config.FutureGenchunksVersion)
        {
            _sapi.Event.GameWorldSave += DeclareFutureGenchunksVersionOnSave;
        }
    }

    /// <summary>Erases the sidecar entirely, simulating a world with no schema versioning at all.</summary>
    private void StripSchemaSidecarOnSave() =>
        _sapi.WorldManager.SaveGame.StoreData(SchemaSidecarKey, System.Array.Empty<byte>());

    /// <summary>
    /// Bumps the sidecar's entry for "manifold:genchunks" to version 99 (this build only supports
    /// up to 1), leaving every other entry Manifold just wrote alone. Also snapshots the
    /// "manifold:genchunks" bytes Manifold just wrote under <see cref="PrevGenchunksKey"/>, so a
    /// restart scenario can prove those exact bytes are still there after the reboot reads a
    /// sidecar it must refuse.
    /// </summary>
    private void DeclareFutureGenchunksVersionOnSave()
    {
        var tree = new TreeAttribute();
        byte[]? raw = _sapi.WorldManager.SaveGame.GetData(SchemaSidecarKey);
        if (raw is { Length: > 0 })
        {
            tree.FromBytes(raw);
        }

        tree.SetInt(GeneratedColumnsKey, 99);
        _sapi.WorldManager.SaveGame.StoreData(SchemaSidecarKey, tree.ToBytes());

        byte[]? genchunks = _sapi.WorldManager.SaveGame.GetData(GeneratedColumnsKey);
        if (genchunks is { Length: > 0 })
        {
            _sapi.WorldManager.SaveGame.StoreData(PrevGenchunksKey, genchunks);
        }
    }
}
