using System;
using Manifold.Api;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Transitions;

/// <summary>Built-in <see cref="ITargetPositionResolver"/> implementations.</summary>
public static class TargetPositionResolvers
{
    /// <summary>Default resolver: keeps player's current X/Z, recomputes Y as surface height in the target dim.</summary>
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

            var current = entity.Pos.AsBlockPos;
            int x = current.X;
            int z = current.Z;

            // Scan downward in the TARGET dimension for the highest non-air block; land just above it.
            // A new BlockPos is constructed each iteration to ensure dimension encoding is correct.
            const int scanTop = 160;
            for (int y = scanTop; y >= 1; y--)
            {
                var probe = new BlockPos(x, y, z, target.InternalId);
                var block = api.World.BlockAccessor.GetBlock(probe);
                if (block is not null && block.Id != 0)
                {
                    return new BlockPos(x, y + 1, z, target.InternalId);
                }
            }

            // No solid ground found (e.g. a void dimension) - keep the player's current Y.
            return new BlockPos(x, current.Y, z, target.InternalId);
        }
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
