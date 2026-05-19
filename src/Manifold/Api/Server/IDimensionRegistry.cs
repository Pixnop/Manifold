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

    /// <summary>Gets an event raised after a dimension is removed via <see cref="TryRemove"/>.</summary>
    event EventHandler<DimensionDestroyedEventArgs> Destroyed;

    /// <summary>Current snapshot of registered dimensions (Active, Pending, and Quarantined).</summary>
    IReadOnlyCollection<IDimension> All { get; }

    /// <summary>Find a dimension by code.</summary>
    /// <param name="code">The code to look up.</param>
    /// <returns>The dimension, or <c>null</c> if not found.</returns>
    IDimension? Get(AssetLocation code);

    /// <summary>Start a fluent declaration. Caller's mod id is captured from the calling <c>ModSystem</c>.</summary>
    /// <param name="code">The new dimension's code.</param>
    /// <returns>A single-use builder.</returns>
    IDimensionBuilder Define(AssetLocation code);

    /// <summary>
    /// Remove an Ephemeral dimension. Throws if the code is not found, refers to a Persistent or BuiltIn dim,
    /// or to a Pending/Quarantined entry.
    /// </summary>
    /// <param name="code">The dimension to remove.</param>
    /// <returns><c>true</c> if removed; <c>false</c> if code not found.</returns>
    bool TryRemove(AssetLocation code);
}
