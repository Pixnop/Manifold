using System;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Shared best-effort overworld teleport used to evacuate an occupant before a dimension is
/// removed or purged.
/// </summary>
internal static class OverworldRescue
{
    /// <summary>
    /// Teleports <paramref name="player"/> to the overworld's last-visited position. An exception,
    /// or a subscriber cancelling the transit, is reported as <c>false</c> (logged via
    /// <paramref name="logger"/> when given) rather than thrown - the caller must not destroy or
    /// remove a dimension out from under a player this returns <c>false</c> for.
    /// </summary>
    /// <param name="transit">Transit service to teleport through.</param>
    /// <param name="player">Occupant to rescue.</param>
    /// <param name="logger">Optional logger for a failed or cancelled rescue.</param>
    /// <returns><c>true</c> if the player actually left their dimension.</returns>
    public static bool TryEvacuate(ITransitionService transit, IServerPlayer player, ILogger? logger)
    {
        bool moved;
        try
        {
            moved = transit.TryTeleportPlayer(
                player,
                DimensionRegistry.OverworldCode,
                new TransitionOptions { SpawnBehavior = SpawnBehavior.LastVisited });
        }
        catch (Exception ex)
        {
            logger?.Warning("[Manifold] Failed to rescue {0} to the overworld: {1}", player.PlayerName, ex.Message);
            return false;
        }

        if (!moved)
        {
            logger?.Warning(
                "[Manifold] Rescue of {0} to the overworld was cancelled; they remain in their dimension.",
                player.PlayerName);
        }

        return moved;
    }
}
