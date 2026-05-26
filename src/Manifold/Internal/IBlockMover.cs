using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Moves a block (with its <c>BlockEntity</c> tree-attribute snapshot, if any) from one
/// dimension-encoded position to another. Source slot is cleared after a successful move.
/// </summary>
/// <remarks>
/// Server-side, main thread. Abstracted so <see cref="TransitService"/> stays unit-testable;
/// the concrete <see cref="BlockMover"/> is engine glue and excluded from coverage.
/// </remarks>
internal interface IBlockMover
{
    /// <summary>
    /// Moves the block at <paramref name="source"/> (dimension-encoded) to <paramref name="target"/>
    /// (dimension-encoded). On success the source slot is set to air. The block's <c>BlockEntity</c>
    /// state is round-tripped through tree attributes, so inventories, attributes and BE-behaviors
    /// follow the block.
    /// </summary>
    /// <param name="source">Source position; <see cref="BlockPos.dimension"/> is the source dim.</param>
    /// <param name="target">Target position; <see cref="BlockPos.dimension"/> is the target dim.</param>
    /// <returns><c>true</c> if a non-air block was moved; <c>false</c> if the source was air.</returns>
    bool Move(BlockPos source, BlockPos target);
}
