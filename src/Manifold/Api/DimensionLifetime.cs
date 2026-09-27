namespace Manifold.Api;

/// <summary>
/// Lifetime semantics of a dimension known to Manifold's <c>IDimensionRegistry</c> (defined in <c>Manifold.Api.Server</c>).
/// </summary>
public enum DimensionLifetime
{
    /// <summary>The built-in overworld (id 0). Cannot be removed.</summary>
    BuiltIn,

    /// <summary>
    /// Survives across sessions; chunks persist with the savegame. Can be registered at boot
    /// (<c>RegisterStatic</c>) or at runtime (<c>Create().Persistent()</c>). After a restart it is
    /// <see cref="DimensionState.Pending"/> until the owning mod calls <c>Define</c> again with the
    /// same code.
    /// </summary>
    Persistent,

    /// <summary>
    /// Created at runtime. Reaped automatically when its last occupant transits out, and discarded on
    /// server shutdown; chunks are never persisted. Disconnecting does not reap it (a logged-out
    /// player reconnects back into it while the server is up). Use <see cref="Persistent"/> for a
    /// runtime dimension that must survive a restart.
    /// </summary>
    Ephemeral,
}
