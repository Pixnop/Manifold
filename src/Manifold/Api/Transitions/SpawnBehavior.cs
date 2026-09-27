namespace Manifold.Api.Transitions;

/// <summary>How a player's landing position is chosen when transiting INTO a dimension.</summary>
public enum SpawnBehavior
{
    /// <summary>Keep the player's current X/Z; land on the surface (default).</summary>
    SameCoordinates,

    /// <summary>
    /// Land at the dimension's fixed spawn point (<c>WithFixedSpawn</c>), used as-is with no surface
    /// search. If none is configured, falls back to <see cref="TargetPositionResolvers.SameXZSurfaceY"/>
    /// and logs a warning once per dimension.
    /// </summary>
    DimensionSpawn,

    /// <summary>Return to where the player last was in this dimension; first visit falls back to SameCoordinates.</summary>
    LastVisited,
}
