using Vintagestory.API.MathTools;

namespace Manifold.Api.Transitions;

/// <summary>
/// Where a player came from when they last entered a dimension through Manifold: the dimension they
/// left, the exact position they left it at, and the way they faced. Read it with
/// <see cref="Server.ITransitionService.GetOrigin"/>; use it with
/// <see cref="Server.ITransitionService.TryReturnPlayer"/>.
/// </summary>
/// <param name="Dimension">The dimension the player left.</param>
/// <param name="X">World X the player left from.</param>
/// <param name="Y">World Y (dimension-local) the player left from.</param>
/// <param name="Z">World Z the player left from.</param>
/// <param name="Yaw">The yaw, in radians, the player was facing when they left.</param>
public sealed record TransitOrigin(IDimension Dimension, double X, double Y, double Z, float Yaw)
{
    /// <summary>Gets the position the player left, as a new vector on every call (the exact doubles, not a block position).</summary>
    public Vec3d Position => new(X, Y, Z);
}
