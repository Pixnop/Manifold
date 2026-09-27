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
}
