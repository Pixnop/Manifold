using System;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Events;

/// <summary>
/// Raised on the server after the destination region has been generated but before the player is
/// teleported into it. Cancellable: setting <see cref="Cancel"/> aborts the transit and leaves the
/// player in the source dimension. Optional <see cref="CancellationReason"/> is reported back to
/// the caller via the <see cref="Server.ITransitionService"/> log.
/// </summary>
/// <remarks>
/// This is the right hook for setup work that needs the target region loaded (placing a welcome
/// block, attaching a server-side state object, computing a custom landing position). For work
/// that does not need the target region, use <c>PlayerEntering</c> (fires earlier, before the
/// region exists).
/// </remarks>
public sealed class PlayerArrivingDimensionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="PlayerArrivingDimensionEventArgs"/> class.</summary>
    /// <param name="player">Player.</param>
    /// <param name="source">Source dimension.</param>
    /// <param name="target">Target dimension.</param>
    /// <param name="targetPos">Final landing position (dimension already encoded).</param>
    public PlayerArrivingDimensionEventArgs(
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

    /// <summary>Final landing position; the target region is already generated at this point.</summary>
    public BlockPos TargetPosition { get; }

    /// <summary>Set to <c>true</c> to cancel the transit after region generation.</summary>
    public bool Cancel { get; set; }

    /// <summary>Optional explanation set by a handler that cancels the transit.</summary>
    public string? CancellationReason { get; set; }
}
