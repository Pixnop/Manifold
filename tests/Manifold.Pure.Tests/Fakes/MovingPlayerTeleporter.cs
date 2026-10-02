using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Pure.Tests.Fakes;

/// <summary>
/// An <see cref="Manifold.Internal.IPlayerTeleporter"/> that applies the move to the player's entity
/// position (instantly, where the engine does it a few ticks later) and records how it was asked to,
/// so a test can chain transits and read where the player ended up.
/// </summary>
internal sealed class MovingPlayerTeleporter : Manifold.Internal.IPlayerTeleporter
{
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

    private void Move(IServerPlayer player, int dimension, double x, double y, double z, float? yaw)
    {
        LastYaw = yaw;
        var pos = player.Entity.Pos;
        pos.Dimension = dimension;
        pos.SetPos(x, y, z);
        if (yaw is { } facing)
        {
            pos.Yaw = facing;
        }
    }
}
