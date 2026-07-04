using System;
using System.Collections.Generic;
using Manifold.Api.Events;
using Vintagestory.API.Common;

namespace Manifold.Api.Client;

/// <summary>Read-only view of Manifold state on the client side.</summary>
/// <remarks>Client-side. All members callable from the main render/game thread.</remarks>
public interface IManifoldClient
{
    /// <summary>Raised when the client mirror learns about a new dimension.</summary>
    event EventHandler<DimensionCreatedEventArgs> Created;

    /// <summary>Raised when the client mirror learns about a removed dimension.</summary>
    event EventHandler<DimensionDestroyedEventArgs> Destroyed;

    /// <summary>
    /// Reserved for a future release: intended to fire when the local player transits to a new
    /// dimension. NOT yet raised in this version (the client has no local player handle to populate
    /// the event args) - do not depend on it. Subscribe to <see cref="Created"/>/<see cref="Destroyed"/>
    /// for dimension state, or use the engine's own client player events for local-player hooks.
    /// </summary>
    event EventHandler<PlayerEnteredDimensionEventArgs> LocalPlayerTransited;

    /// <summary>All dimensions known to the client mirror.</summary>
    IReadOnlyCollection<IDimension> Dimensions { get; }

    /// <summary>True if Manifold loaded healthily on the server (Harmony patches OK).</summary>
    bool IsHealthy { get; }

    /// <summary>Find a dimension by code.</summary>
    /// <param name="code">Asset code.</param>
    /// <returns>The dimension or <c>null</c>.</returns>
    IDimension? Get(AssetLocation code);
}
