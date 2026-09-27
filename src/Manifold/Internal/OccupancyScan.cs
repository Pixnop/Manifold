using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Shared "which connected players are currently in this dimension" scan, by engine id. Used by the
/// registry's occupancy guard (<see cref="DimensionRegistry.TryRemove"/>), forced-removal evacuation
/// (<see cref="ManifoldServerFacade.ForceRemoveDimension"/>), the admin purge command's evacuation,
/// and <see cref="ManifoldServerFacade.GetPlayersIn"/>, so each does not walk
/// <c>AllOnlinePlayers</c> with its own copy of the position check.
/// </summary>
internal static class OccupancyScan
{
    /// <summary><c>true</c> if <paramref name="player"/>'s live position is inside the dimension with the given engine id.</summary>
    /// <param name="player">Player to check.</param>
    /// <param name="internalId">Engine dimension id to match.</param>
    /// <returns><c>true</c> if the player is in that dimension.</returns>
    public static bool IsIn(IServerPlayer player, int internalId) =>
        EntityPosAccess.PosOrNull(player.Entity)?.Dimension == internalId;

    /// <summary>Online players (from <c>sapi.World.AllOnlinePlayers</c>) currently inside the dimension with the given engine id.</summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="internalId">Engine dimension id to match.</param>
    /// <returns>Matching players, in <c>AllOnlinePlayers</c> order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sapi"/> is null.</exception>
    public static IEnumerable<IServerPlayer> PlayersIn(ICoreServerAPI sapi, int internalId)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        foreach (var p in sapi.World.AllOnlinePlayers)
        {
            if (p is IServerPlayer sp && IsIn(sp, internalId))
            {
                yield return sp;
            }
        }
    }

    /// <summary><c>true</c> if at least one online player is inside the dimension with the given engine id.</summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="internalId">Engine dimension id to match.</param>
    /// <returns><c>true</c> if occupied; <c>false</c> if empty.</returns>
    public static bool IsOccupied(ICoreServerAPI sapi, int internalId) => PlayersIn(sapi, internalId).Any();
}
