using System;
using Manifold.Api.Events;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Manifold.Api.Server;

/// <summary>Primitive transit operations between dimensions.</summary>
/// <remarks>Server-side. <see cref="TeleportPlayer"/> must be invoked on the main thread.</remarks>
public interface ITransitionService
{
    /// <summary>Raised before transit completes; set <c>Cancel = true</c> to abort.</summary>
    event EventHandler<PlayerEnteringDimensionEventArgs> PlayerEntering;

    /// <summary>Raised after the player has entered the target dimension.</summary>
    event EventHandler<PlayerEnteredDimensionEventArgs> PlayerEntered;

    /// <summary>Raised after the player has left the source dimension.</summary>
    event EventHandler<PlayerLeftDimensionEventArgs> PlayerLeft;

    /// <summary>
    /// Teleport a player to the dimension identified by <paramref name="targetDim"/>.
    /// Raises <see cref="PlayerEntering"/> (cancellable), then <see cref="PlayerLeft"/> and <see cref="PlayerEntered"/>.
    /// </summary>
    /// <param name="player">Server player to teleport.</param>
    /// <param name="targetDim">Target dimension code.</param>
    /// <param name="options">Optional transit settings.</param>
    /// <exception cref="Manifold.Api.DimensionNotFoundException">Target code unknown.</exception>
    /// <exception cref="Manifold.Api.DimensionStateException">Target is not Active.</exception>
    /// <exception cref="Manifold.Api.ManifoldUnhealthyException">Manifold's Harmony patches failed at boot.</exception>
    void TeleportPlayer(IServerPlayer player, AssetLocation targetDim, TransitionOptions options = default);
}
