using System;
using Vintagestory.API.Server;

namespace Manifold.Api.Events;

/// <summary>Raised on the server after a player has crossed to a target dimension.</summary>
public sealed class PlayerEnteredDimensionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="PlayerEnteredDimensionEventArgs"/> class.</summary>
    /// <param name="player">Player.</param>
    /// <param name="source">Source dimension.</param>
    /// <param name="target">Target dimension.</param>
    public PlayerEnteredDimensionEventArgs(IServerPlayer player, IDimension source, IDimension target)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Player = player;
        SourceDimension = source;
        TargetDimension = target;
    }

    /// <summary>Player that crossed.</summary>
    public IServerPlayer Player { get; }

    /// <summary>Dimension the player came from.</summary>
    public IDimension SourceDimension { get; }

    /// <summary>Dimension the player is now in.</summary>
    public IDimension TargetDimension { get; }
}
