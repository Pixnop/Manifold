using System;
using System.Collections.Generic;
using System.Linq;

namespace Manifold.Internal;

/// <summary>
/// Pure planning logic for streaming worldgen. Given the players to stream around, a predicate
/// telling whether a column is already loaded, and a per-dimension budget function, returns the
/// columns to ensure this tick: nearest-first, deduplicated, negative coordinates clipped,
/// independently capped per dimension so one busy dim cannot starve another.
/// </summary>
/// <remarks>No engine dependency; fully unit-testable.</remarks>
internal static class StreamingPlanner
{
    /// <summary>Computes the columns to ensure this tick.</summary>
    /// <param name="players">Players currently in streaming dimensions.</param>
    /// <param name="isLoaded">Predicate: is the column (dim, cx, cz) already loaded.</param>
    /// <param name="budgetForDim">Per-dimension column budget. Returns the maximum number of columns to plan for the given dimension on this tick.</param>
    /// <returns>Columns to ensure (nearest player first within each dimension), each dimension capped at its own budget.</returns>
    public static IReadOnlyList<PlannedColumn> Plan(
        IReadOnlyList<StreamingPlayer> players,
        Func<int, int, int, bool> isLoaded,
        Func<int, int> budgetForDim)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(isLoaded);
        ArgumentNullException.ThrowIfNull(budgetForDim);

        var candidates = new Dictionary<(int Dim, int Cx, int Cz), (long DistSq, List<string> Uids)>();
        foreach (var p in players)
        {
            CollectWindow(p, isLoaded, candidates);
        }

        return candidates
            .GroupBy(kvp => kvp.Key.Dim)
            .OrderBy(g => g.Key)
            .SelectMany(g => g
                .OrderBy(kvp => kvp.Value.DistSq)
                .ThenBy(kvp => kvp.Key.Cx)
                .ThenBy(kvp => kvp.Key.Cz)
                .Take(Math.Max(0, budgetForDim(g.Key)))
                .Select(kvp => new PlannedColumn(kvp.Key.Dim, kvp.Key.Cx, kvp.Key.Cz, kvp.Value.Uids)))
            .ToList();
    }

    /// <summary>Adds every not-yet-loaded column in a player's square window to the candidate set.</summary>
    private static void CollectWindow(
        StreamingPlayer p,
        Func<int, int, int, bool> isLoaded,
        Dictionary<(int Dim, int Cx, int Cz), (long DistSq, List<string> Uids)> candidates)
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
                AddCandidate(candidates, (p.DimId, cx, cz), distSq, p.PlayerUid);
            }
        }
    }

    /// <summary>Records a candidate column, merging the requesting player and keeping the nearest distance.</summary>
    private static void AddCandidate(
        Dictionary<(int Dim, int Cx, int Cz), (long DistSq, List<string> Uids)> candidates,
        (int Dim, int Cx, int Cz) key,
        long distSq,
        string uid)
    {
        if (!candidates.TryGetValue(key, out var existing))
        {
            candidates[key] = (distSq, new List<string> { uid });
            return;
        }

        if (!existing.Uids.Contains(uid))
        {
            existing.Uids.Add(uid);
        }

        candidates[key] = (Math.Min(distSq, existing.DistSq), existing.Uids);
    }
}
