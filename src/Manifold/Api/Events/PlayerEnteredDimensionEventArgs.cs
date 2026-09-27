using System;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Events;

/// <summary>Raised on the server after a player has crossed to a target dimension.</summary>
public sealed class PlayerEnteredDimensionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="PlayerEnteredDimensionEventArgs"/> class.</summary>
    /// <param name="player">Player.</param>
    /// <param name="source">Source dimension.</param>
    /// <param name="target">Target dimension.</param>
    /// <param name="targetPosition">Landing position the player was sent to.</param>
    public PlayerEnteredDimensionEventArgs(IServerPlayer player, IDimension source, IDimension target, BlockPos targetPosition)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetPosition);
        Player = player;
        SourceDimension = source;
        TargetDimension = target;
        TargetPosition = targetPosition;
    }

    /// <summary>Player that crossed.</summary>
    public IServerPlayer Player { get; }

    /// <summary>Dimension the player came from.</summary>
    public IDimension SourceDimension { get; }

    /// <summary>Dimension the player is now in.</summary>
    public IDimension TargetDimension { get; }

    /// <summary>
    /// Landing position the player was sent to, in the target dimension. Read this rather than the
    /// player entity's position: the engine applies the move once the destination chunks load, a
    /// few ticks after this event, so the entity may still report where it came from.
    /// </summary>
    public BlockPos TargetPosition { get; }
}
