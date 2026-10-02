using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Manifold.Api.Events;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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
    /// Never raised (kept for binary compatibility with mods built against earlier versions that
    /// subscribe to it): populating its event args needs an <c>IServerPlayer</c> the client does not
    /// have. Use <see cref="LocalPlayerChangedDimension"/> instead.
    /// </summary>
    [SuppressMessage("Info Code Smell", "S1133:Deprecated code should be removed", Justification = "Kept for binary compatibility with mods built against pre-0.6 versions that subscribe to it; remove in the next major release.")]
    [Obsolete("Never raised; use LocalPlayerChangedDimension.")]
    event EventHandler<PlayerEnteredDimensionEventArgs> LocalPlayerTransited;

    /// <summary>
    /// Raised on the client main thread when the local player changes dimension, and once more when
    /// the player joins a world while already inside a custom dimension. Both are resolved through
    /// the client dimension mirror.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real transit (<see cref="LocalPlayerDimensionChangedEventArgs.IsJoin"/> is <c>false</c>) is
    /// raised after the server's transit notification arrives. It is not raised for a transit whose
    /// source or target dimension code is not (yet) known to the client mirror, for example during a
    /// rare resync race.
    /// </para>
    /// <para>
    /// The join notification (<see cref="LocalPlayerDimensionChangedEventArgs.IsJoin"/> is
    /// <c>true</c>) is raised at most once per client session, as soon as both the manifest snapshot
    /// has reached the mirror and the local player entity exists with its position, whichever comes
    /// last. Its <see cref="LocalPlayerDimensionChangedEventArgs.Source"/> is synthetic: the mirror's
    /// overworld, standing in for "where the player was before", because the player did not actually
    /// leave it. <see cref="LocalPlayerDimensionChangedEventArgs.Target"/> is the dimension the player
    /// is in and <see cref="LocalPlayerDimensionChangedEventArgs.TargetPosition"/> their current block
    /// position. It is not raised when the player joins in the overworld (start from the overworld
    /// state), and not at all if the player's dimension id is unknown to the mirror (a warning is
    /// logged). A player who logs in inside a quarantined dimension (its owning mod is gone) gets the
    /// join notification for that dimension, typically followed by a real transit to the overworld
    /// when the server rescues them. A real transit that arrives before the join notification has been
    /// decided cancels it, so a stale join is never raised on top of it. A mod that subscribes after
    /// the join has already happened does not receive it; ask <see cref="GetDimensionOf"/> for the
    /// local player entity instead.
    /// </para>
    /// </remarks>
    event EventHandler<LocalPlayerDimensionChangedEventArgs> LocalPlayerChangedDimension;

    /// <summary>All dimensions known to the client mirror.</summary>
    IReadOnlyCollection<IDimension> Dimensions { get; }

    /// <summary>Always <c>true</c>. Kept for compatibility with mods compiled against earlier versions.</summary>
    bool IsHealthy { get; }

    /// <summary>Find a dimension by code.</summary>
    /// <param name="code">Asset code.</param>
    /// <returns>The dimension or <c>null</c>.</returns>
    IDimension? Get(AssetLocation code);

    /// <summary>
    /// Finds a mirrored dimension by its engine dimension id (<see cref="IDimension.InternalId"/>).
    /// The overworld is id 0.
    /// </summary>
    /// <param name="internalId">Engine dimension id, as found in an entity's <c>Pos.Dimension</c> or a block position.</param>
    /// <returns>The dimension, or <c>null</c> if the mirror does not know that id (yet).</returns>
    IDimension? GetByInternalId(int internalId);

    /// <summary>
    /// Finds the mirrored dimension containing <paramref name="entity"/>, by its position's engine
    /// dimension id (0 is always the overworld).
    /// </summary>
    /// <param name="entity">The entity to locate.</param>
    /// <returns>The dimension, or <c>null</c> if its id is not (yet) known to the client mirror.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    IDimension? GetDimensionOf(Entity entity);
}
