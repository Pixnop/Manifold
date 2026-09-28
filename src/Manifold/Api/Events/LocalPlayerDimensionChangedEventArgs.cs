using System;
using Vintagestory.API.MathTools;

namespace Manifold.Api.Events;

/// <summary>
/// Raised on the client after the local player has transited to a new dimension.
/// </summary>
/// <remarks>
/// Resolved client-side from the server's transit notification through the client dimension
/// mirror; see <see cref="Client.IManifoldClient.LocalPlayerChangedDimension"/> for when it is
/// (and is not) raised.
/// </remarks>
public sealed class LocalPlayerDimensionChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="LocalPlayerDimensionChangedEventArgs"/> class.</summary>
    /// <param name="source">Dimension the local player left.</param>
    /// <param name="target">Dimension the local player is now in.</param>
    /// <param name="targetPosition">Landing position in <paramref name="target"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="targetPosition"/> is null.</exception>
    public LocalPlayerDimensionChangedEventArgs(IDimension source, IDimension target, BlockPos targetPosition)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetPosition);
        Source = source;
        Target = target;
        TargetPosition = targetPosition;
    }

    /// <summary>Dimension the local player left.</summary>
    public IDimension Source { get; }

    /// <summary>Dimension the local player is now in.</summary>
    public IDimension Target { get; }

    /// <summary>Landing position in <see cref="Target"/>.</summary>
    public BlockPos TargetPosition { get; }
}
