using System;
using Vintagestory.API.MathTools;

namespace Manifold.Api.Events;

/// <summary>
/// Raised on the client after the local player has transited to a new dimension.
/// </summary>
/// <remarks>
/// Resolved client-side from the server's transit notification through the client dimension
/// mirror; see <see cref="Client.IManifoldClient.LocalPlayerChangedDimension"/> for when it is
/// (and is not) raised. When <see cref="IsJoin"/> is <c>true</c> the notification is not a real
/// transit: it reports the dimension the local player was already in when they joined the world.
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

    /// <summary>Initializes a new instance of the <see cref="LocalPlayerDimensionChangedEventArgs"/> class, optionally flagged as a join.</summary>
    /// <param name="source">Dimension the local player left, or the mirror's overworld for a join.</param>
    /// <param name="target">Dimension the local player is now in.</param>
    /// <param name="targetPosition">Landing (or current, for a join) position in <paramref name="target"/>.</param>
    /// <param name="isJoin"><c>true</c> for the notification raised when the player joins already inside <paramref name="target"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="targetPosition"/> is null.</exception>
    internal LocalPlayerDimensionChangedEventArgs(IDimension source, IDimension target, BlockPos targetPosition, bool isJoin)
        : this(source, target, targetPosition)
    {
        IsJoin = isJoin;
    }

    /// <summary>Initializes a new instance of the <see cref="LocalPlayerDimensionChangedEventArgs"/> class, flagged as a join and/or a respawn.</summary>
    /// <param name="source">Dimension the local player left, or the mirror's overworld for a join.</param>
    /// <param name="target">Dimension the local player is now in.</param>
    /// <param name="targetPosition">Landing (or current, for a join) position in <paramref name="target"/>.</param>
    /// <param name="isJoin"><c>true</c> for the notification raised when the player joins already inside <paramref name="target"/>.</param>
    /// <param name="isRespawn"><c>true</c> when the player arrived by dying and respawning.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="targetPosition"/> is null.</exception>
    internal LocalPlayerDimensionChangedEventArgs(IDimension source, IDimension target, BlockPos targetPosition, bool isJoin, bool isRespawn)
        : this(source, target, targetPosition, isJoin)
    {
        IsRespawn = isRespawn;
    }

    /// <summary>
    /// Dimension the local player left. For a join notification (<see cref="IsJoin"/> is <c>true</c>)
    /// this is a synthetic value: the mirror's overworld, even though the player never actually left it.
    /// </summary>
    public IDimension Source { get; }

    /// <summary>Dimension the local player is now in.</summary>
    public IDimension Target { get; }

    /// <summary>
    /// Landing position in <see cref="Target"/>; for a join notification, the local player's current
    /// block position when the notification was raised.
    /// </summary>
    public BlockPos TargetPosition { get; }

    /// <summary>
    /// <c>true</c> when this is the one-time notification raised after the local player joined a
    /// world while already inside a custom dimension (the player did not transit anywhere:
    /// <see cref="Source"/> is then the overworld as a stand-in); <c>false</c> for a real transit.
    /// </summary>
    public bool IsJoin { get; }

    /// <summary>
    /// <c>true</c> when the local player arrived by dying in another dimension and respawning (the
    /// game's respawn does not change the dimension, so Manifold moves them out), <c>false</c> for a
    /// transit and for a join. Added in 0.6.1.
    /// </summary>
    public bool IsRespawn { get; }
}
