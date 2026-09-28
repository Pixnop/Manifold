using System;
using System.Collections.Generic;
using Manifold.Api.Events;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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

    /// <summary>
    /// Raised immediately after Manifold generates a brand-new chunk column for a dimension; never
    /// for a column it only <em>loaded</em> from disk (a restart, or a re-visit of an already
    /// generated column). Use it to decorate or post-process the terrain a worldgen strategy just
    /// produced (a structure, a marker, loot) without changing the strategy itself. Raised on the
    /// server main thread, after the column's blocks are committed, for every generation path: a
    /// player transit, the streaming driver, and <see cref="IManifoldServer.GenerateRegion"/>.
    /// Subscribers are isolated with the same per-subscriber try/catch as <see cref="Created"/> and
    /// <see cref="Destroyed"/>: one throwing subscriber is reported and does not stop the others or
    /// the caller. See <see cref="ColumnGeneratedEventArgs.BlockAccessor"/> for whether writes
    /// need a commit and whether the column has already reached any client.
    /// </summary>
    event EventHandler<ColumnGeneratedEventArgs> ColumnGenerated;

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

    /// <summary>
    /// The registered dimension <paramref name="entity"/> is currently in, resolved from its live
    /// position's dimension id (<c>Entity.Pos.Dimension</c>). Returns the built-in overworld for id 0.
    /// </summary>
    /// <param name="entity">The entity to locate.</param>
    /// <returns>
    /// The dimension the entity is in, or <c>null</c> if its position's dimension id does not match
    /// any dimension currently registered (e.g. one removed since the entity last moved).
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is null.</exception>
    IDimension? GetDimensionOf(Entity entity);
}
