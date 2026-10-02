using System;
using System.Collections.Generic;

namespace Manifold.Internal;

/// <summary>
/// Per-player bookkeeping of teleports the engine applies later. A player teleport is queued by the
/// engine until the destination column is loaded, and a second call does not replace the first: each
/// call runs its own completion, in call order. So after two quick transits the engine still applies
/// the first one's coordinates after the second's. This tracks the latest teleport asked for, whether
/// it has been applied yet, and tells each completion what to do, so the teleporter can keep the
/// latest landing and ignore the superseded ones.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class LandingTracker
{
    private readonly Dictionary<string, State> _players = new();
    private long _lastId;

    /// <summary>Registers a new teleport as the player's latest and not yet applied.</summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <param name="landing">What the teleport asks for.</param>
    /// <returns>An id identifying this teleport, to hand back to <see cref="Complete"/>.</returns>
    public long Begin(string playerUid, PendingLanding landing)
    {
        long id = ++_lastId;
        _players[playerUid] = new State(id, landing);
        return id;
    }

    /// <summary>The player's latest landing when the engine has not applied it yet.</summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <returns>The pending landing, or <c>null</c> when none is waiting.</returns>
    public PendingLanding? GetPending(string playerUid) =>
        _players.TryGetValue(playerUid, out var state) && !state.Applied ? state.Landing : null;

    /// <summary>
    /// Called when the engine has applied teleport <paramref name="id"/> (it just set the entity to that
    /// teleport's coordinates). The latest teleport applies its yaw. A superseded one does not: if the
    /// latest had already been applied, the entity was just moved away from it, so it is issued again
    /// (unless the coordinates happen to be the same); if the latest is still waiting, its own
    /// completion will put the player right, so nothing else is needed.
    /// </summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <param name="id">The id <see cref="Begin"/> returned for the teleport that was applied.</param>
    /// <param name="x">X the entity now has.</param>
    /// <param name="y">Y the entity now has.</param>
    /// <param name="z">Z the entity now has.</param>
    /// <returns>What to do next.</returns>
    public LandingOutcome Complete(string playerUid, long id, double x, double y, double z)
    {
        if (!_players.TryGetValue(playerUid, out var state))
        {
            return default; // forgotten (the player left): nothing to apply
        }

        if (id == state.Id)
        {
            state.Applied = true;
            return new LandingOutcome(state.Landing.Yaw, null);
        }

        bool displaced = state.Applied && (Differs(x, state.Landing.X) || Differs(y, state.Landing.Y) || Differs(z, state.Landing.Z));
        return displaced ? new LandingOutcome(null, state.Landing) : default;
    }

    /// <summary>Drops everything known about a player (they disconnected: their entity is gone, completions that still arrive are ignored).</summary>
    /// <param name="playerUid">Player unique id.</param>
    public void Forget(string playerUid) => _players.Remove(playerUid);

    /// <summary>Whether two coordinates are different places (a tolerance far below one block, not exact equality).</summary>
    private static bool Differs(double a, double b) => Math.Abs(a - b) > 0.001;

    private sealed class State(long id, PendingLanding landing)
    {
        public long Id { get; } = id;

        public PendingLanding Landing { get; } = landing;

        public bool Applied { get; set; }
    }
}
