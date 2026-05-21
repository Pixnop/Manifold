using System;
using System.Collections.Generic;
using System.Linq;

namespace Manifold.Internal;

/// <summary>
/// Pure planning logic for streaming worldgen. Given the players to stream around, a predicate
/// telling whether a column is already loaded, and a per-tick budget, returns the columns to
/// ensure this tick: nearest-first, deduplicated, negative coordinates clipped, capped at the budget.
/// </summary>
/// <remarks>No engine dependency; fully unit-testable.</remarks>
internal static class StreamingPlanner
{
    /// <summary>Computes the columns to ensure this tick.</summary>
    /// <param name="players">Players currently in streaming dimensions.</param>
    /// <param name="isLoaded">Predicate: is the column (dim, cx, cz) already loaded.</param>
    /// <param name="budgetPerTick">Maximum number of columns to return.</param>
    /// <returns>Ordered columns to ensure (nearest player first), capped at the budget.</returns>
    public static IReadOnlyList<PlannedColumn> Plan(
        IReadOnlyList<StreamingPlayer> players,
        Func<int, int, int, bool> isLoaded,
        int budgetPerTick)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(isLoaded);

        var candidates = new Dictionary<(int Dim, int Cx, int Cz), (long DistSq, List<string> Uids)>();

        foreach (var p in players)
        {
            for (int dx = -p.LoadRadius; dx <= p.LoadRadius; dx++)
            {
                for (int dz = -p.LoadRadius; dz <= p.LoadRadius; dz++)
                {
                    int cx = p.ChunkX + dx;
                    int cz = p.ChunkZ + dz;
                    if (cx < 0 || cz < 0 || isLoaded(p.DimId, cx, cz))
                    {
                        continue;
                    }

                    long distSq = ((long)dx * dx) + ((long)dz * dz);
                    var key = (p.DimId, cx, cz);
                    if (candidates.TryGetValue(key, out var existing))
                    {
                        if (!existing.Uids.Contains(p.PlayerUid))
                        {
                            existing.Uids.Add(p.PlayerUid);
                        }

                        if (distSq < existing.DistSq)
                        {
                            candidates[key] = (distSq, existing.Uids);
                        }
                    }
                    else
                    {
                        candidates[key] = (distSq, new List<string> { p.PlayerUid });
                    }
                }
            }
        }

        return candidates
            .OrderBy(kvp => kvp.Value.DistSq)
            .ThenBy(kvp => kvp.Key.Dim)
            .ThenBy(kvp => kvp.Key.Cx)
            .ThenBy(kvp => kvp.Key.Cz)
            .Take(budgetPerTick)
            .Select(kvp => new PlannedColumn(kvp.Key.Dim, kvp.Key.Cx, kvp.Key.Cz, kvp.Value.Uids))
            .ToList();
    }
}
