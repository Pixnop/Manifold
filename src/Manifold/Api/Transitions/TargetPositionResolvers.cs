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
    /// Default resolver: keeps the player's current X/Z; recomputes Y as the first solid block
    /// below the target dimension's ceiling that has two blocks of passable space above it (feet
    /// and head), so the landing spot is never inside a wall and never under a one-block gap in a
    /// cave ceiling. A liquid block is not landed on directly: the search continues below it for
    /// dry ground, and the liquid's surface is only used if the column has no dry spot at all. If
    /// the column has no valid spot whatsoever (fully solid top to bottom, entirely empty/void, or
    /// out of the world's height range), the player's current Y is kept unchanged.
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

            // Highest liquid block seen while scanning, if any: used only as a last-resort landing
            // spot (see the fallback below), never picked ahead of a dry one.
            int? liquidSurfaceY = null;

            // Scan downward in the TARGET dimension for the first solid, non-liquid block that has
            // two clear blocks above it (feet, then head), and land just above it. Start from the
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
                    liquidSurfaceY ??= y;
                    continue;
                }

                if (y + 2 <= top
                    && IsPassable(BlockAt(api, x, y + 1, z, dim))
                    && IsPassable(BlockAt(api, x, y + 2, z, dim)))
                {
                    return new BlockPos(x, y + 1, z, dim);
                }
            }

            // No dry spot with clearance anywhere in the column. Land on the highest liquid surface
            // seen, if any - still better than being buried in terrain - otherwise there is truly
            // nothing to land on (a void dimension, a fully solid column, or an empty one), so keep
            // the caller's current Y unchanged.
            return liquidSurfaceY is { } liquidY
                ? new BlockPos(x, liquidY + 1, z, dim)
                : new BlockPos(x, current.Y, z, dim);
        }

        /// <summary>A block is passable (can occupy feet or head space) only if it is air.</summary>
        private static bool IsPassable(Block? block) => block is null || block.Id == 0;

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
