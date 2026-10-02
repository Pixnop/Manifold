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
        : this(player, source, target, false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PlayerLeftDimensionEventArgs"/> class, optionally flagged as a respawn.</summary>
    /// <param name="player">Player.</param>
    /// <param name="source">Source dimension.</param>
    /// <param name="target">Target dimension.</param>
    /// <param name="isRespawn"><c>true</c> when the player left by dying and respawning rather than by a transit.</param>
    public PlayerLeftDimensionEventArgs(IServerPlayer player, IDimension source, IDimension target, bool isRespawn)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Player = player;
        SourceDimension = source;
        TargetDimension = target;
        IsRespawn = isRespawn;
    }

    /// <summary>Player that left.</summary>
    public IServerPlayer Player { get; }

    /// <summary>Dimension the player left.</summary>
    public IDimension SourceDimension { get; }

    /// <summary>Dimension the player went to.</summary>
    public IDimension TargetDimension { get; }

    /// <summary>
    /// <c>true</c> when the player left the dimension by dying and respawning (the game's respawn
    /// does not change the dimension, so Manifold moves the player out), <c>false</c> for a transit.
    /// A respawn raises this event and <see cref="PlayerEnteredDimensionEventArgs"/> but not
    /// <c>PlayerEntering</c> or <c>PlayerArriving</c>, since it cannot be refused.
    /// </summary>
    public bool IsRespawn { get; }
}
