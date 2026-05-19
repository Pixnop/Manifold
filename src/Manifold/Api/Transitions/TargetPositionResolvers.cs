using System;
using Manifold.Api;
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
        public BlockPos Resolve(IServerPlayer player, IDimension target, ICoreServerAPI api)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(api);
            var pos = player.Entity.Pos.AsBlockPos;
            int? surface = api.World.BlockAccessor.GetTerrainMapheightAt(pos);
            int y = surface.HasValue ? surface.Value + 1 : pos.Y;
            return new BlockPos(pos.X, y, pos.Z, target.InternalId);
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

        public BlockPos Resolve(IServerPlayer player, IDimension target, ICoreServerAPI api) =>
            new(_pos.X, _pos.Y, _pos.Z, target.InternalId);
    }
}
