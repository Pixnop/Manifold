using System;
using Vintagestory.API.Server;

namespace Manifold.Api.Events;

/// <summary>Raised on the server after a player has left a dimension.</summary>
public sealed class PlayerLeftDimensionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="PlayerLeftDimensionEventArgs"/> class.</summary>
    /// <param name="player">Player.</param>
    /// <param name="source">Source dimension.</param>
    /// <param name="target">Target dimension.</param>
    public PlayerLeftDimensionEventArgs(IServerPlayer player, IDimension source, IDimension target)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Player = player;
        SourceDimension = source;
        TargetDimension = target;
    }

    /// <summary>Player that left.</summary>
    public IServerPlayer Player { get; }

    /// <summary>Dimension the player left.</summary>
    public IDimension SourceDimension { get; }

    /// <summary>Dimension the player went to.</summary>
    public IDimension TargetDimension { get; }
}
