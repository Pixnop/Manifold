using System.Collections.Generic;
using Manifold.Internal;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Pure.Tests.Fakes;

/// <summary>
/// An <see cref="IPlayerTeleporter"/> that behaves like the production one on top of the engine's
/// teleport: the dimension flips straight away, and the coordinates are applied either at once (the
/// engine does that when the destination column is loaded) or, with <see cref="Defer"/> set, later,
/// when a test calls <see cref="LandNext"/> or <see cref="LandAll"/>. Deferred teleports are applied in
/// call order and a later one never replaces an earlier one, exactly like the engine's queue. Each
/// application runs the same completion logic as production (<see cref="LandingTracker"/>): the latest
/// teleport sets its yaw, a superseded one that displaced the player is undone by issuing the latest
/// landing again.
/// </summary>
internal sealed class MovingPlayerTeleporter : IPlayerTeleporter
{
    private readonly LandingTracker _tracker = new();
    private readonly Queue<(IServerPlayer Player, long Id, PendingLanding Landing)> _queue = new();

    /// <summary>Gets or sets a value indicating whether teleports wait for <see cref="LandNext"/> instead of applying at once.</summary>
    public bool Defer { get; set; }

    /// <summary>Gets the number of block-position teleports received.</summary>
    public int BlockCalls { get; private set; }

    /// <summary>Gets the number of exact teleports received.</summary>
    public int ExactCalls { get; private set; }

    /// <summary>Gets the yaw the last teleport (of either kind) was asked to apply.</summary>
    public float? LastYaw { get; private set; }

    /// <summary>Gets the number of teleports still waiting for <see cref="LandNext"/>.</summary>
    public int QueuedCount => _queue.Count;

    /// <inheritdoc/>
    public void Teleport(IServerPlayer player, BlockPos target, float? yaw = null)
    {
        BlockCalls++;
        Move(player, target.dimension, new PendingLanding(target.X + 0.5, target.Y, target.Z + 0.5, yaw));
    }

    /// <inheritdoc/>
    public void TeleportExact(IServerPlayer player, int dimension, double x, double y, double z, float? yaw)
    {
        ExactCalls++;
        Move(player, dimension, new PendingLanding(x, y, z, yaw));
    }

    /// <inheritdoc/>
    public PendingLanding? GetPendingLanding(IServerPlayer player) => _tracker.GetPending(player.PlayerUID);

    /// <inheritdoc/>
    public void Forget(IServerPlayer player) => _tracker.Forget(player.PlayerUID);

    /// <summary>Applies the oldest queued teleport, like the engine does once its destination column loads.</summary>
    public void LandNext()
    {
        var (player, id, landing) = _queue.Dequeue();
        Apply(player, id, landing);
    }

    /// <summary>Applies every queued teleport, oldest first (teleports they issue again while doing so are applied too).</summary>
    public void LandAll()
    {
        while (_queue.Count > 0)
        {
            LandNext();
        }
    }

    private void Move(IServerPlayer player, int dimension, PendingLanding landing)
    {
        LastYaw = landing.Yaw;
        player.Entity.Pos.Dimension = dimension;
        Issue(player, landing);
    }

    private void Issue(IServerPlayer player, PendingLanding landing)
    {
        long id = _tracker.Begin(player.PlayerUID, landing);
        if (Defer)
        {
            _queue.Enqueue((player, id, landing));
            return;
        }

        Apply(player, id, landing);
    }

    private void Apply(IServerPlayer player, long id, PendingLanding landing)
    {
        var pos = player.Entity.Pos;
        pos.SetPos(landing.X, landing.Y, landing.Z);
        var outcome = _tracker.Complete(player.PlayerUID, id, pos.X, pos.Y, pos.Z);
        if (outcome.ApplyYaw is { } yaw)
        {
            pos.Yaw = yaw;
        }

        if (outcome.Reissue is { } again)
        {
            Issue(player, again);
        }
    }
}
