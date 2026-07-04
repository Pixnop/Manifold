using System;
using Manifold.Internal.Util;
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

        // Use the binary-compat accessor: Entity.Pos is a field on 1.21.x and a property on 1.22.x,
        // and direct field/property access would mismatch one of the two at runtime.
        var pos = EntityPosAccess.Pos(entity);
        pos.Dimension = dim;
        pos.SetPos(x, y, z);
        pos.Motion.Set(0, 0, 0);
        entity.IsTeleport = true; // do not interpolate the next position packet client-side

        // The dimension is encoded into the chunk Y (dim * 1024 chunk rows). Newer API builds mark the
        // three-arg ChunkIndex3D obsolete in favor of a dimension-aware overload that is not present in
        // all 1.21 builds; suppress here to keep one form that compiles and runs on every 1.21.x.
#pragma warning disable CS0618 // Type or member is obsolete
        long chunkIndex = _sapi.World.ChunkProvider.ChunkIndex3D(
            x / ChunkMath.ChunkSize, (y / ChunkMath.ChunkSize) + (dim * ChunkMath.DimensionChunkYStride), z / ChunkMath.ChunkSize);
#pragma warning restore CS0618
        _sapi.World.UpdateEntityChunk(entity, chunkIndex);
    }
}
