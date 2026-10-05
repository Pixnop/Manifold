using System;
using System.Collections.Generic;
using System.Linq;
using Manifold.Internal.Util;

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
    /// <summary>
    /// How many chunks a player's window extends past the radius the engine sends them. The engine's
    /// send ring asks for an overworld column wherever it finds a dimension column not loaded yet, so a
    /// window that stopped exactly at the send radius would race the ring at its edge each time the
    /// player crosses a chunk border. One chunk of lead keeps that edge generated a border ahead.
    /// The lead never takes a window past the server's <c>MaxChunkRadius</c>, which is also what a
    /// window used to be for every player.
    /// </summary>
    internal const int WindowLead = 1;

    /// <summary>
    /// The chunk radius to keep generated around a player: the radius the engine sends to them
    /// (their view distance in chunks, rounded up, capped at the server's <c>MaxChunkRadius</c>), plus
    /// <see cref="WindowLead"/> where that stays within <c>MaxChunkRadius</c>, and never below the
    /// dimension's configured <paramref name="loadRadius"/>.
    /// A view distance that is zero or negative means unknown and falls back to the server's radius.
    /// </summary>
    /// <param name="loadRadius">The dimension's configured streaming radius (floor).</param>
    /// <param name="viewDistanceBlocks">The player's approved view distance in blocks; zero or negative when unknown.</param>
    /// <param name="maxChunkRadius">The server's maximum chunk radius.</param>
    /// <returns>The window radius in chunks.</returns>
    public static int WindowRadius(int loadRadius, int viewDistanceBlocks, int maxChunkRadius)
    {
        int sent = viewDistanceBlocks > 0
            ? Math.Min(maxChunkRadius, (viewDistanceBlocks + ChunkMath.ChunkSize - 1) / ChunkMath.ChunkSize)
            : maxChunkRadius;
        return Math.Max(loadRadius, Math.Min(sent + WindowLead, maxChunkRadius));
    }

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
