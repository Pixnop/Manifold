using System;
using System.Collections.Generic;
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
    private readonly Dictionary<string, PendingLanding> _pending = new();

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

        // Step 2: positional teleport. The engine tests whether the DIMENSION 0 column at this X/Z is
        // loaded: if so it applies the move right away, if not it queues it until that column loads.
        // Either way it then runs the callback, so the pending landing is recorded first and cleared
        // there, and the yaw is set there too: after the move and after the engine bumped the player's
        // position version, so a late packet from the client's old orientation cannot undo it. The
        // engine never pushes a player's yaw to their own client (its camera is client-driven):
        // TransitService's PlayerEntered handler sends it through Manifold's channel instead.
        var landing = new PendingLanding(x, y, z, yaw);
        string uid = player.PlayerUID;
        _pending[uid] = landing;
        player.Entity.TeleportToDouble(x, y, z, () =>
        {
            if (yaw is { } facing)
            {
                EntityPosAccess.Pos(player.Entity).Yaw = facing;
            }

            // A newer teleport may have replaced this one before it was applied: leave that one pending.
            if (_pending.TryGetValue(uid, out var current) && current == landing)
            {
                _pending.Remove(uid);
            }
        });
    }

    /// <inheritdoc/>
    public PendingLanding? GetPendingLanding(IServerPlayer player)
    {
        if (!_pending.TryGetValue(player.PlayerUID, out var landing))
        {
            return null;
        }

        // The engine's own flag is the truth: a callback that never ran (the player disconnected
        // before the column loaded) must not leave a landing pending forever.
        if (player.Entity.Teleporting)
        {
            return landing;
        }

        _pending.Remove(player.PlayerUID);
        return null;
    }
}
