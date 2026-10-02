namespace Manifold.Api.Transitions;

/// <summary>Where a player who dies inside a dimension comes back when they respawn.</summary>
/// <remarks>
/// The engine's respawn teleports a player with X/Y/Z only and never changes their dimension, so left
/// alone a player who dies in a custom dimension would respawn in it, at the overworld spawn's
/// coordinates. Manifold sends them out instead; this chooses where to. A death in the overworld is
/// never touched.
/// </remarks>
public enum RespawnBehavior
{
    /// <summary>
    /// The player respawns in the overworld, at the position the game itself chose for the respawn
    /// (the temporal gear they used or a spawn an admin or role set, else the world spawn). The dimension's game mode and inventory
    /// policies are undone as on any other way out. This is the default.
    /// </summary>
    Overworld,

    /// <summary>
    /// The player respawns inside the dimension, at its fixed spawn point (<c>WithFixedSpawn</c>), with
    /// no transit: no event is raised and no policy changes. For arenas, hubs and other dimensions
    /// that keep their players. If no spawn point is configured, falls back to
    /// <see cref="Overworld"/> and logs a warning once per dimension.
    /// </summary>
    DimensionSpawn,
}
