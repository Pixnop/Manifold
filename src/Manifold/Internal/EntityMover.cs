using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Default <see cref="IEntityMover"/>. Re-homes an entity into another dimension the same way
/// <c>EntityPlayer.ChangeDimension</c> does internally, using only public API.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class EntityMover : IEntityMover
{
    private readonly ICoreServerAPI _sapi;

    /// <summary>Initializes a new instance of the <see cref="EntityMover"/> class.</summary>
    /// <param name="sapi">Server API.</param>
    public EntityMover(ICoreServerAPI sapi) =>
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));

    /// <inheritdoc/>
    public void Move(Entity entity, BlockPos targetWithDimension)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(targetWithDimension);

        int dim = targetWithDimension.dimension;
        int x = targetWithDimension.X;
        int y = targetWithDimension.Y;
        int z = targetWithDimension.Z;

        entity.Pos.Dimension = dim;
        entity.Pos.SetPos(x, y, z);
        entity.Pos.Motion.Set(0, 0, 0);
        entity.IsTeleport = true; // do not interpolate the next position packet client-side

        long chunkIndex = _sapi.World.ChunkProvider.ChunkIndex3D(x / 32, (y / 32) + (dim * 1024), z / 32);
        _sapi.World.UpdateEntityChunk(entity, chunkIndex);
    }
}
