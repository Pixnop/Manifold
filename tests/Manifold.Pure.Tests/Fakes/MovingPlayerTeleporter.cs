using System.Collections.Generic;
using Manifold.Internal;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Pure.Tests.Fakes;

/// <summary>
/// An <see cref="IPlayerTeleporter"/> that applies the move to the player's entity position and
/// records how it was asked to, so a test can chain transits and read where the player ended up.
/// By default the move is applied at once (the engine does that when the destination column is
/// already loaded); with <see cref="Defer"/> set it behaves like the engine waiting for a column:
/// the dimension flips straight away but the coordinates and yaw only change on <see cref="Land"/>.
/// </summary>
internal sealed class MovingPlayerTeleporter : IPlayerTeleporter
{
    private readonly Dictionary<string, (int Dimension, PendingLanding Landing)> _pending = new();

    /// <summary>Gets or sets a value indicating whether moves wait for <see cref="Land"/> instead of applying at once.</summary>
    public bool Defer { get; set; }

    /// <summary>Gets the number of block-position teleports received.</summary>
    public int BlockCalls { get; private set; }

    /// <summary>Gets the number of exact teleports received.</summary>
    public int ExactCalls { get; private set; }

    /// <summary>Gets the yaw the last teleport (of either kind) was asked to apply.</summary>
    public float? LastYaw { get; private set; }

    /// <inheritdoc/>
    public void Teleport(IServerPlayer player, BlockPos target, float? yaw = null)
    {
        BlockCalls++;
        Move(player, target.dimension, target.X + 0.5, target.Y, target.Z + 0.5, yaw);
    }

    /// <inheritdoc/>
    public void TeleportExact(IServerPlayer player, int dimension, double x, double y, double z, float? yaw)
    {
        ExactCalls++;
        Move(player, dimension, x, y, z, yaw);
    }

    /// <inheritdoc/>
    public PendingLanding? GetPendingLanding(IServerPlayer player) =>
        _pending.TryGetValue(player.PlayerUID, out var entry) ? entry.Landing : null;

    /// <summary>Applies the player's deferred landing, if any, like the engine does once the destination column loads.</summary>
    /// <param name="player">Player whose landing to apply.</param>
    public void Land(IServerPlayer player)
    {
        if (_pending.Remove(player.PlayerUID, out var entry))
        {
            Apply(player, entry.Landing);
        }
    }

    private void Move(IServerPlayer player, int dimension, double x, double y, double z, float? yaw)
    {
        LastYaw = yaw;
        player.Entity.Pos.Dimension = dimension;
        var landing = new PendingLanding(x, y, z, yaw);
        if (Defer)
        {
            _pending[player.PlayerUID] = (dimension, landing);
            return;
        }

        Apply(player, landing);
    }

    private static void Apply(IServerPlayer player, PendingLanding landing)
    {
        var pos = player.Entity.Pos;
        pos.SetPos(landing.X, landing.Y, landing.Z);
        if (landing.Yaw is { } facing)
        {
            pos.Yaw = facing;
        }
    }
}
