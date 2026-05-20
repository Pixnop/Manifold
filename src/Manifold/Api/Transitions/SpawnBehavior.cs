namespace Manifold.Api.Transitions;

/// <summary>How a player's landing position is chosen when transiting INTO a dimension.</summary>
public enum SpawnBehavior
{
    /// <summary>Keep the player's current X/Z; land on the surface (default).</summary>
    SameCoordinates,

    /// <summary>Always land at the dimension's configured fixed spawn point.</summary>
    DimensionSpawn,

    /// <summary>Return to where the player last was in this dimension; first visit falls back to SameCoordinates.</summary>
    LastVisited,
}
