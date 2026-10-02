using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Transitions;
using Manifold.Internal.Util;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// The respawn half of <see cref="TransitService"/>: where a player who died in a custom dimension
/// comes back. The engine's respawn handler picks a spawn position and moves the player there with
/// <c>EntityPlayer.TeleportTo(EntityPos)</c>, which sets X/Y/Z and never the dimension, then revives
/// them. By the time this runs the player is alive again and standing at the engine's chosen
/// coordinates, still in the dimension they died in.
/// </summary>
internal sealed partial class TransitService
{
    /// <summary>
    /// An entity position's <c>InternalY</c> is its Y plus this times its dimension id. The engine's
    /// temporal gear stores a respawn point through <c>XYZInt</c>, which is that internal Y, so a spawn
    /// set inside a custom dimension comes back from the engine as a Y at or above this: the dimension
    /// and the local Y packed into one number.
    /// </summary>
    internal const double DimensionYStride = 32768;

    private readonly HashSet<int> _warnedRespawnWithoutSpawnPoint = new();

    /// <summary>
    /// Splits a spawn Y that may carry a dimension (see <see cref="DimensionYStride"/>) into the
    /// dimension it designates and the local Y. A plain Y designates the overworld.
    /// </summary>
    /// <param name="rawY">The Y the engine's respawn left on the entity.</param>
    /// <returns>The designated dimension id (0 for none) and the local Y.</returns>
    internal static (int Dimension, double Y) DecodeSpawnY(double rawY) =>
        rawY >= DimensionYStride
            ? ((int)(rawY / DimensionYStride), rawY % DimensionYStride)
            : (0, rawY);

    /// <summary>
    /// Takes a player who has just respawned out of the custom dimension they died in, or keeps them
    /// in it at its fixed spawn when it opted into <see cref="RespawnBehavior.DimensionSpawn"/>.
    /// A death in the overworld is not touched, unless the game's spawn designates a dimension (see
    /// <see cref="DecodeSpawnY"/>), which the game's respawn leaves the player stranded above.
    /// </summary>
    /// <remarks>
    /// Leaving is a real transit for everything but its cancellable half: the target's game mode and
    /// inventory policies are applied, the old dimension's are undone, and <c>PlayerLeft</c> and
    /// <c>PlayerEntered</c> are raised (flagged <c>IsRespawn</c>; none when the dimension they died in
    /// is not one Manifold manages). <c>PlayerEntering</c> and <c>PlayerArriving</c> are not: a respawn
    /// cannot be refused, and a veto would strand the player in the dimension they died in. No
    /// dismount (death already unmounted them), and no last-visited position is recorded: the engine's
    /// spawn coordinates are not a place the player walked to. The origin recorded for the dimension
    /// they land in is dropped, so <c>TryReturnPlayer</c> cannot send them back to where they died. A
    /// respawn that stays in the dimension moves the player and nothing else. Nothing of the player is
    /// rewritten before the move itself, so a call that throws can be repeated.
    /// </remarks>
    /// <param name="player">A player who is alive again.</param>
    /// <returns><c>true</c> if the player was moved; <c>false</c> when they died in the overworld with a plain spawn.</returns>
    internal bool RespawnPlayer(IServerPlayer player)
    {
        var pos = EntityPosAccess.Pos(player.Entity);
        int sourceId = pos.Dimension;
        if (sourceId == 0 && pos.Y < DimensionYStride)
        {
            return false;
        }

        var died = _registry.GetByInternalId(sourceId);
        var plan = PlanRespawn(died, pos);
        var target = _registry.GetByInternalId(plan.Dimension)!;
        if (plan.Dimension != 0)
        {
            _generator.EnsureRegion(_sapi, plan.Dimension, ChunkMath.ToChunk(plan.X), ChunkMath.ToChunk(plan.Z), player);
        }

        // The entity must hold a plain Y before it is re-homed: a spawn that carried a dimension left
        // a Y far above any dimension's height, and the re-homing indexes the entity's chunk by it.
        double rawY = pos.Y;
        pos.Y = plan.Y;
        try
        {
            _movers.Player.TeleportExact(player, plan.Dimension, plan.X, plan.Y, plan.Z, null);
        }
        catch
        {
            pos.Y = rawY; // leave the spawn the way the game left it, so the move can be repeated
            throw;
        }

        if (plan.Dimension != sourceId)
        {
            FinishRespawnOut(player, died, target, plan);
        }

        _sapi.Logger?.Notification(
            "[Manifold] {0} respawned in '{1}' after dying in dimension {2}.", player.PlayerName, target.Code, sourceId);
        return true;
    }

    /// <summary>The part of a respawn that changes dimension: the target's policies, then the events (none from a dimension Manifold does not manage).</summary>
    private void FinishRespawnOut(IServerPlayer player, DimensionImpl? died, DimensionImpl target, RespawnPlan plan)
    {
        _positionStore.Origins.Remove(player.PlayerUID, plan.Dimension);
        ApplyPolicies(player, target, target);
        if (died is not null)
        {
            var landing = new BlockPos((int)Math.Floor(plan.X), (int)Math.Floor(plan.Y), (int)Math.Floor(plan.Z), plan.Dimension);
            RaiseMoved(player, died, target, landing, null, isRespawn: true);
        }
    }

    /// <summary>
    /// Where a respawn lands, in order: the dead player's own dimension when it keeps its dead and has
    /// a fixed spawn; the dimension the engine's spawn designates when it packs one into its Y and that
    /// dimension is Active and Persistent (an ephemeral dimension's id is recycled, so a spawn set in
    /// one could name an unrelated dimension by now); the overworld at the engine's coordinates
    /// otherwise. A designated dimension that does not qualify falls back to the world's default spawn
    /// in the overworld.
    /// </summary>
    private RespawnPlan PlanRespawn(DimensionImpl? died, EntityPos pos)
    {
        if (died is { State: DimensionState.Active, RespawnBehavior: RespawnBehavior.DimensionSpawn })
        {
            if (died.SpawnPoint is { } spawn)
            {
                return new RespawnPlan(died.InternalId, spawn.X + 0.5, spawn.Y, spawn.Z + 0.5);
            }

            WarnRespawnWithoutSpawnPoint(died);
        }

        var (designated, y) = DecodeSpawnY(pos.Y);
        if (designated == 0 || _registry.GetByInternalId(designated) is { State: DimensionState.Active, Lifetime: DimensionLifetime.Persistent })
        {
            return new RespawnPlan(designated, pos.X, y, pos.Z);
        }

        _sapi.Logger?.Warning(
            "[Manifold] A respawn point designates dimension {0}, which is not an active persistent dimension; using the world spawn instead.",
            designated);
        return _sapi.World.DefaultSpawnPosition is { } world
            ? new RespawnPlan(0, world.X, world.Y, world.Z)
            : new RespawnPlan(0, pos.X, y, pos.Z);
    }

    private void WarnRespawnWithoutSpawnPoint(DimensionImpl died)
    {
        if (_warnedRespawnWithoutSpawnPoint.Add(died.InternalId))
        {
            _sapi.Logger?.Warning(
                "[Manifold] Dimension '{0}' uses RespawnBehavior.DimensionSpawn but has no configured spawn "
                + "point (WithFixedSpawn); its dead respawn in the overworld.",
                died.Code);
        }
    }

    private readonly record struct RespawnPlan(int Dimension, double X, double Y, double Z);
}
