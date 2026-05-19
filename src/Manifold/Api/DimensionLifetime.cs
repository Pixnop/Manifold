namespace Manifold.Api;

/// <summary>
/// Lifetime semantics of a dimension known to Manifold's <c>IDimensionRegistry</c> (defined in <c>Manifold.Api.Server</c>).
/// </summary>
public enum DimensionLifetime
{
    /// <summary>The built-in overworld (id 0). Cannot be removed.</summary>
    BuiltIn,

    /// <summary>Declared at boot, survives across sessions. Chunks persist with the savegame.</summary>
    Persistent,

    /// <summary>Created at runtime, discarded on server shutdown.</summary>
    Ephemeral,
}
