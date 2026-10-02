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
    /// A death in the overworld is not touched at all.
    /// </summary>
    /// <remarks>
    /// Leaving is a real transit for everything but its cancellable half: the target's game mode and
    /// inventory policies are applied, the old dimension's are undone, and <c>PlayerLeft</c> and
    /// <c>PlayerEntered</c> are raised. <c>PlayerEntering</c> and <c>PlayerArriving</c> are not: a
    /// respawn cannot be refused, and a veto would strand the player in the dimension they died in.
    /// No dismount (death already unmounted them), and no origin or last-visited position is recorded:
    /// the engine's spawn coordinates are not a place the player walked to. A respawn that stays in the
    /// dimension moves the player and nothing else.
    /// </remarks>
    /// <param name="player">A player who is alive again.</param>
    /// <returns><c>true</c> if the player was moved; <c>false</c> when they are in the overworld.</returns>
    internal bool RespawnPlayer(IServerPlayer player)
    {
        var pos = EntityPosAccess.Pos(player.Entity);
        int sourceId = pos.Dimension;
        if (sourceId == 0)
        {
            return false;
        }

        var died = _registry.GetByInternalId(sourceId);
        var plan = PlanRespawn(died, pos);

        // The entity must hold a plain Y before it is re-homed: a spawn that carried a dimension left
        // a Y far above any dimension's height, and the re-homing indexes the entity's chunk by it.
        pos.Y = plan.Y;
        if (plan.Dimension != 0)
        {
            _generator.EnsureRegion(_sapi, plan.Dimension, ChunkMath.ToChunk(plan.X), ChunkMath.ToChunk(plan.Z), player);
        }

        _movers.Player.TeleportExact(player, plan.Dimension, plan.X, plan.Y, plan.Z, null);

        var target = _registry.GetByInternalId(plan.Dimension)!;
        if (plan.Dimension != sourceId)
        {
            var landing = new BlockPos((int)Math.Floor(plan.X), (int)Math.Floor(plan.Y), (int)Math.Floor(plan.Z), plan.Dimension);
            CompleteTransit(player, (IDimension?)died ?? _registry.GetByInternalId(0)!, target, target, landing, null);
        }

        _sapi.Logger?.Notification(
            "[Manifold] {0} respawned in '{1}' after dying in dimension {2}.", player.PlayerName, target.Code, sourceId);
        return true;
    }

    /// <summary>
    /// Where a respawn lands, in order: the dead player's own dimension when it keeps its dead and has
    /// a fixed spawn; the dimension the engine's spawn designates when it packs one into its Y; the
    /// overworld at the engine's coordinates otherwise. A designated dimension that is gone or not
    /// active falls back to the world's default spawn in the overworld.
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
        if (designated == 0 || _registry.GetByInternalId(designated) is { State: DimensionState.Active })
        {
            return new RespawnPlan(designated, pos.X, y, pos.Z);
        }

        _sapi.Logger?.Warning(
            "[Manifold] A respawn point designates dimension {0}, which is not active; using the world spawn instead.",
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
