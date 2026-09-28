using System;
using Manifold.Api;
using Manifold.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Transitions;

/// <summary>Built-in <see cref="ITargetPositionResolver"/> implementations.</summary>
public static class TargetPositionResolvers
{
    /// <summary>
    /// Default resolver: keeps the player's current X/Z; recomputes Y as the first solid, dry
    /// block below the target dimension's ceiling that has two blocks of passable space above it
    /// (feet and head), so the landing spot is never inside a wall, never under a one-block gap in
    /// a cave ceiling, and never on top of a decoration with no floor to stand on (tall grass, a
    /// vine, a torch). The first liquid block found while scanning down is the top of that liquid
    /// body: the search never continues past it into a seabed or buried rock, so it lands on the
    /// liquid's own surface (with the same two-block clearance check) if the column has no dry spot
    /// above it, or falls through to the column-empty fallback otherwise. If the column has no valid
    /// spot whatsoever (fully solid top to bottom, entirely empty/void, or out of the world's height
    /// range), the player's current Y is kept unchanged.
    /// </summary>
    public static ITargetPositionResolver SameXZSurfaceY { get; } = new SameXZSurfaceYResolver();

    /// <summary>Returns a constant <see cref="BlockPos"/> for every transit.</summary>
    /// <param name="pos">Fixed landing position.</param>
    /// <returns>Resolver that always returns <paramref name="pos"/> (dimension overridden to target).</returns>
    public static ITargetPositionResolver FixedSpawn(BlockPos pos) => new FixedSpawnResolver(pos);

    private sealed class SameXZSurfaceYResolver : ITargetPositionResolver
    {
        public BlockPos Resolve(Entity entity, IDimension target, ICoreServerAPI api)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(api);

            var current = EntityPosAccess.Pos(entity).AsBlockPos;
            int x = current.X;
            int z = current.Z;
            int dim = target.InternalId;
            int top = api.WorldManager.MapSizeY - 1;

            // The liquid surface candidate, if the column's top liquid body has clearance above it:
            // used only as a last-resort landing spot (see the fallback below), never picked ahead
            // of a dry one.
            int? liquidSurfaceY = null;

            // Scan downward in the TARGET dimension for the first solid, dry block that has two
            // clear blocks above it (feet, then head), and land just above it. Start from the
            // world's actual ceiling, not a fixed constant: terrain taller than a constant would
            // scan starting inside the mountain and land the player in it. A one-block gap under a
            // cave ceiling fails the two-block clearance check and is skipped, same as solid rock.
            for (int y = top; y >= 1; y--)
            {
                var block = BlockAt(api, x, y, z, dim);
                if (block is null || block.Id == 0)
                {
                    continue;
                }

                if (block.IsLiquid())
                {
                    // This is the top of a liquid body (a lake, an ocean): real terrain never has
                    // dry ground floating below open liquid, only the liquid's own bed and then
                    // solid rock. Searching further down would surface a player in a buried,
                    // sealed cave under the seabed instead of at the water, so the scan stops here:
                    // the liquid's surface (with clearance) is the only candidate left, or the
                    // column-empty fallback if it has none.
                    if (HasHeadroom(api, x, y, z, dim, top))
                    {
                        liquidSurfaceY = y;
                    }

                    break;
                }

                if (!HasFloor(block))
                {
                    // Tall grass, a vine, a torch, a reed: nothing to stand on. Walk through it, same
                    // as air, and keep scanning down for a real floor.
                    continue;
                }

                if (HasHeadroom(api, x, y, z, dim, top))
                {
                    return new BlockPos(x, y + 1, z, dim);
                }
            }

            // No dry spot with clearance anywhere in the column. Land on the liquid surface found,
            // if it had clearance (still better than being buried in terrain); otherwise there is
            // truly nothing to land on (a void dimension, a fully solid column, an empty one, or a
            // liquid body capped by solid rock with no room above it), so keep the caller's current
            // Y unchanged.
            return liquidSurfaceY is { } liquidY
                ? new BlockPos(x, liquidY + 1, z, dim)
                : new BlockPos(x, current.Y, z, dim);
        }

        /// <summary>Whether the two blocks above <paramref name="y"/> (feet, then head) are passable and inside the world.</summary>
        private static bool HasHeadroom(ICoreServerAPI api, int x, int y, int z, int dim, int top) =>
            y + 2 <= top
            && IsPassable(BlockAt(api, x, y + 1, z, dim))
            && IsPassable(BlockAt(api, x, y + 2, z, dim));

        /// <summary>A block is passable (can occupy feet or head space) if it is air or has no floor to stand on.</summary>
        private static bool IsPassable(Block? block) =>
            block is null || block.Id == 0 || (!block.IsLiquid() && !HasFloor(block));

        /// <summary>
        /// A block counts as ground only if something would actually stop a player standing on it:
        /// a real collision box, or a flagged solid top face. Decorations with no collision (tall
        /// grass, vines, torches, reeds) do not.
        /// </summary>
        private static bool HasFloor(Block block) =>
            block.SideSolid[BlockFacing.UP.Index] || block.CollisionBoxes is { Length: > 0 };

        // A new BlockPos is constructed for every probe to ensure dimension encoding is correct.
        private static Block? BlockAt(ICoreServerAPI api, int x, int y, int z, int dim) =>
            api.World.BlockAccessor.GetBlock(new BlockPos(x, y, z, dim));
    }

    private sealed class FixedSpawnResolver : ITargetPositionResolver
    {
        private readonly BlockPos _pos;

        public FixedSpawnResolver(BlockPos pos)
        {
            ArgumentNullException.ThrowIfNull(pos);
            _pos = pos;
        }

        public BlockPos Resolve(Entity entity, IDimension target, ICoreServerAPI api) =>
            new(_pos.X, _pos.Y, _pos.Z, target.InternalId);
    }
}
