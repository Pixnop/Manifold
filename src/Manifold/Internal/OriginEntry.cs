namespace Manifold.Internal;

/// <summary>Where a player came from when they entered a dimension: the dimension they left, the exact position and their yaw.</summary>
/// <param name="SourceId">Engine id of the dimension the player left.</param>
/// <param name="SourceCode">Code of the dimension the player left; checked on lookup so a recycled engine id is never mistaken for it.</param>
/// <param name="X">World X the player left from.</param>
/// <param name="Y">World Y (dimension-local) the player left from.</param>
/// <param name="Z">World Z the player left from.</param>
/// <param name="Yaw">The yaw, in radians, the player was facing.</param>
internal readonly record struct OriginEntry(int SourceId, string SourceCode, double X, double Y, double Z, float Yaw);
