using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Default <see cref="IBlockMover"/>. Snapshots the source block (and its <c>BlockEntity</c> via
/// <c>ToTreeAttributes</c>), writes it at the target, restores the BE via <c>FromTreeAttributes</c>,
/// then clears the source. Uses only public API.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class BlockMover : IBlockMover
{
    // Vintage Story embeds the dimension into a block entity's stored Y coordinate:
    // internalY = localY + dimension * 32768 (32 blocks * 1024 chunk rows per dimension).
    // SetAndCorrectDimension decodes it back. We re-encode the target position into the
    // serialized tree so the rehydrated BlockEntity knows it lives at the destination.
    private const int DimensionYStride = 32768;

    private readonly ICoreServerAPI _sapi;

    /// <summary>Initializes a new instance of the <see cref="BlockMover"/> class.</summary>
    /// <param name="sapi">Server API.</param>
    public BlockMover(ICoreServerAPI sapi) =>
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));

    /// <inheritdoc/>
    public bool Move(BlockPos source, BlockPos target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        // Moving a block onto its own position is a no-op: writing then clearing the same slot would
        // delete the block. Guard before any SetBlock so we never destroy what we were asked to move.
        if (source.dimension == target.dimension && source.X == target.X && source.Y == target.Y && source.Z == target.Z)
        {
            return true;
        }

        var world = _sapi.World;
        var accessor = world.BlockAccessor;

        int sourceId = accessor.GetBlockId(source);
        if (sourceId == 0)
        {
            // Air at source: nothing to move. Treat as a no-op (callers can act on the bool).
            return false;
        }

        // Snapshot the BE state, if any. ToTreeAttributes is the documented save/sync path and
        // captures inventory, attributes, and BE-behavior state in one tree - including the source
        // position (posx/posy/posz).
        TreeAttribute? beTree = null;
        var sourceBe = accessor.GetBlockEntity(source);
        if (sourceBe is not null)
        {
            beTree = new TreeAttribute();
            sourceBe.ToTreeAttributes(beTree);

            // Re-stamp the embedded position to the target. Without this the rehydrated BlockEntity
            // keeps the source coordinates and the engine cannot route interactions to it (e.g. a
            // chest teleports but cannot be opened). posy is dimension-encoded.
            beTree.SetInt("posx", target.X);
            beTree.SetInt("posy", target.Y + (target.dimension * DimensionYStride));
            beTree.SetInt("posz", target.Z);
        }

        // Write the block at the target. SetBlock spawns a BE if the block declares one, which we
        // then rehydrate from the captured (position-corrected) tree.
        accessor.SetBlock(sourceId, target);
        if (beTree is not null)
        {
            var targetBe = accessor.GetBlockEntity(target);
            targetBe?.FromTreeAttributes(beTree, world);
            targetBe?.MarkDirty(true);
        }

        // Clear the source slot. Done after the target write so an exception mid-flight leaves
        // the source intact (the worst case is a duplicated block, never a lost one).
        accessor.SetBlock(0, source);
        return true;
    }
}
