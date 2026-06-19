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

    /// <summary>
    /// Created at runtime. Reaped automatically when its last occupant transits out, and discarded on
    /// server shutdown; chunks are never persisted. Disconnecting does not reap it (a logged-out
    /// player reconnects back into it while the server is up). Use <see cref="Persistent"/> for a
    /// runtime dimension that must survive a restart.
    /// </summary>
    Ephemeral,
}
