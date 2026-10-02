namespace Manifold.Internal;

/// <summary>
/// Where a player teleport asked to put the player, before the engine has applied it. The engine
/// moves the player only once the destination column is loaded, which can be several ticks after
/// the call, so until then the entity still reports the position it left.
/// </summary>
/// <param name="X">World X the teleport asked for.</param>
/// <param name="Y">World Y (dimension-local) the teleport asked for.</param>
/// <param name="Z">World Z the teleport asked for.</param>
/// <param name="Yaw">The yaw the teleport asked for, or <c>null</c> if the player keeps theirs.</param>
internal readonly record struct PendingLanding(double X, double Y, double Z, float? Yaw);
