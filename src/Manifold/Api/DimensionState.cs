namespace Manifold.Api;

/// <summary>
/// Runtime state of a dimension within Manifold's registry.
/// </summary>
public enum DimensionState
{
    /// <summary>Available for read/write/transit operations.</summary>
    Active,

    /// <summary>Known to Manifold from a prior savegame but its owning mod has not (yet) re-registered it.</summary>
    Pending,

    /// <summary>
    /// Owning mod is no longer loaded. Chunks preserved on disk, worldgen suspended,
    /// transit refused. Use the admin command <c>/manifold purge &lt;code&gt;</c> to release.
    /// </summary>
    Quarantined,
}
