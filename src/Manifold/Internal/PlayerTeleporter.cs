using System;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Production <see cref="IPlayerTeleporter"/> backed by VS engine cross-dimension teleport.</summary>
/// <remarks>
/// Cross-dimension transit requires <c>EntityPlayer.ChangeDimension</c> BEFORE positional teleport.
/// Using <c>TeleportTo(EntityPos)</c> or <c>TeleportTo(int, int, int)</c> alone does NOT change dimension -
/// they call <c>LoadChunkColumnPriority</c> without a dimension parameter and the player remains in dim 0.
/// <c>ChangeDimension</c> additionally fires the public <c>IEventAPI.PlayerDimensionChanged</c> event.
/// </remarks>
internal sealed class PlayerTeleporter : IPlayerTeleporter
{
    /// <inheritdoc/>
    public void Teleport(IServerPlayer player, BlockPos target, float? yaw = null)
    {
        // +0.5 centers the player on the target block in X/Z.
        TeleportExact(player, target.dimension, target.X + 0.5, target.Y, target.Z + 0.5, yaw);
    }

    /// <inheritdoc/>
    public void TeleportExact(IServerPlayer player, int dimension, double x, double y, double z, float? yaw)
    {
        // Step 1: rebind entity to the destination dimension (chunk membership + PlayerDimensionChanged event).
        player.Entity.ChangeDimension(dimension);

        // Step 2: positional teleport. The engine applies it once the destination column is loaded and
        // then runs the callback: the yaw is set there, after the move and after the engine bumped the
        // player's position version, so a late packet from the client's old orientation cannot undo it.
        // The engine never pushes a player's yaw to their own client (its camera is client-driven):
        // TransitService's PlayerEntered handler sends it through Manifold's channel instead.
        Action? applyYaw = yaw is { } facing ? () => EntityPosAccess.Pos(player.Entity).Yaw = facing : null;
        player.Entity.TeleportToDouble(x, y, z, applyYaw);
    }
}
