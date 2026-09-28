namespace AtlasFixture;

/// <summary>
/// Optional boot configuration for the fixture, loaded from ModConfig/atlasfixture.json.
/// Scenario classes stage the file per class with Atlas's [AtlasDataFiles]; an absent file
/// means all defaults, so classes that stage nothing are unaffected.
/// </summary>
public sealed class AtlasFixtureConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether the fixture seeds the runtime persistence
    /// dimensions (keeper, ghost) at boot; see AtlasFixtureModSystem.SeedPersistenceFixtures.
    /// </summary>
    public bool SeedPersistenceFixtures { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the fixture appends a manifest entry owned by a
    /// mod that is not installed, so the next boot seeds it Quarantined; see
    /// AtlasFixtureModSystem.InjectOrphanManifestEntry.
    /// </summary>
    public bool SeedOrphan { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the fixture erases Manifold's "manifold:schema"
    /// sidecar on every save, simulating a world that has never run against a Manifold build with
    /// schema versioning at all; see AtlasFixtureModSystem.StripSchemaSidecarOnSave.
    /// </summary>
    public bool StripSchemaSidecar { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the fixture bumps the sidecar entry for
    /// "manifold:genchunks" to an unrecognized future version on every save; see
    /// AtlasFixtureModSystem.DeclareFutureGenchunksVersionOnSave.
    /// </summary>
    public bool FutureGenchunksVersion { get; set; }
}
