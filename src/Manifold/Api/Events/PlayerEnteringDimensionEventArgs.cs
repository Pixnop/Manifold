using System;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Events;

/// <summary>Raised on the server before a player crosses to a target dimension. Cancellable.</summary>
public sealed class PlayerEnteringDimensionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="PlayerEnteringDimensionEventArgs"/> class.</summary>
    /// <param name="player">Player.</param>
    /// <param name="source">Source dimension.</param>
    /// <param name="target">Target dimension.</param>
    /// <param name="targetPos">Target position.</param>
    public PlayerEnteringDimensionEventArgs(
        IServerPlayer player, IDimension source, IDimension target, BlockPos targetPos)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetPos);
        Player = player;
        SourceDimension = source;
        TargetDimension = target;
        TargetPosition = targetPos;
    }

    /// <summary>Player undergoing transit.</summary>
    public IServerPlayer Player { get; }

    /// <summary>Dimension the player is leaving.</summary>
    public IDimension SourceDimension { get; }

    /// <summary>Dimension the player is entering.</summary>
    public IDimension TargetDimension { get; }

    /// <summary>Target position (dimension already encoded in <c>BlockPos</c>).</summary>
    public BlockPos TargetPosition { get; }

    /// <summary>Set to <c>true</c> to cancel the transit.</summary>
    public bool Cancel { get; set; }
}
