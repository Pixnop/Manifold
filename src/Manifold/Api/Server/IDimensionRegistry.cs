using System;
using System.Collections.Generic;
using Manifold.Api.Events;
using Vintagestory.API.Common;

namespace Manifold.Api.Server;

/// <summary>
/// Source of truth for dimensions known to Manifold.
/// </summary>
/// <remarks>
/// Server-side. Mutations must run on the main thread; reads are snapshot-safe from any thread.
/// </remarks>
public interface IDimensionRegistry
{
    /// <summary>Gets an event raised after a dimension becomes Active.</summary>
    event EventHandler<DimensionCreatedEventArgs> Created;

    /// <summary>
    /// Gets an event raised after a dimension is removed from the registry: via
    /// <see cref="TryRemove"/>, the auto-reap of an emptied Ephemeral dimension,
    /// <see cref="IManifoldServer.ForceRemoveDimension"/>, shutdown cleanup of Ephemeral dimensions,
    /// or the admin <c>/manifold purge</c> command (which can also remove Persistent and Quarantined
    /// dimensions).
    /// </summary>
    event EventHandler<DimensionDestroyedEventArgs> Destroyed;

    /// <summary>Current snapshot of registered dimensions (Active, Pending, and Quarantined).</summary>
    IReadOnlyCollection<IDimension> All { get; }

    /// <summary>Find a dimension by code.</summary>
    /// <param name="code">The code to look up.</param>
    /// <returns>The dimension, or <c>null</c> if not found.</returns>
    IDimension? Get(AssetLocation code);

    /// <summary>
    /// Start a fluent declaration. Must be called through an owner-scoped registry obtained via
    /// <c>sapi.GetManifoldServer(thisModSystem).Registry</c>; calling on the parameterless
    /// overload's registry throws <see cref="DimensionOwnerRequiredException"/>.
    /// </summary>
    /// <param name="code">The new dimension's code.</param>
    /// <returns>A single-use builder.</returns>
    /// <exception cref="Manifold.Api.DimensionOwnerRequiredException">
    /// Thrown when the registry cannot determine the owning mod id (e.g. the unscoped facade).
    /// </exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="code"/> is null.</exception>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="code"/> is not <c>domain:path</c> with both segments matching <c>[a-z0-9_]+</c>,
    /// or uses the reserved <c>manifold</c> domain.
    /// </exception>
    /// <exception cref="Manifold.Api.DimensionAlreadyRegisteredException">
    /// <paramref name="code"/> is already registered in this boot, or is pending under a different
    /// owner mod.
    /// </exception>
    IDimensionBuilder Define(AssetLocation code);

    /// <summary>
    /// Remove an Ephemeral dimension. Must be called on the main thread. Refuses (returns
    /// <c>false</c>) while a player is still inside it - a dimension is never destroyed out from under
    /// its occupants. Move everyone out first, or use <see cref="IManifoldServer.ForceRemoveDimension"/>
    /// to evacuate occupants then remove.
    /// </summary>
    /// <param name="code">The dimension to remove.</param>
    /// <returns><c>true</c> if removed; <c>false</c> if the code is not found or the dimension is occupied.</returns>
    /// <exception cref="DimensionBuiltInImmutableException">The dimension is the built-in overworld.</exception>
    /// <exception cref="DimensionStateException">The dimension is Persistent (use the admin purge command).</exception>
    bool TryRemove(AssetLocation code);
}
