using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Api.Worldgen;

/// <summary>Provided to <see cref="IWorldgenStrategy.GenerateColumn"/> for one chunk column.</summary>
/// <remarks>Server-side, main thread. Write blocks via <see cref="BlockAccessor"/> using dimension-encoded positions.</remarks>
public interface IWorldgenChunkContext
{
    /// <summary>Engine dimension id being generated.</summary>
    int DimensionId { get; }

    /// <summary>Chunk X index (multiply by 32 for world X).</summary>
    int ChunkX { get; }

    /// <summary>Chunk Z index (multiply by 32 for world Z).</summary>
    int ChunkZ { get; }

    /// <summary>
    /// Block accessor for writing terrain. Positions MUST be dimension-encoded
    /// (use <c>new BlockPos(x, y, z, DimensionId)</c>). This is a bulk accessor: writes are staged
    /// and committed after <see cref="IWorldgenStrategy.GenerateColumn"/> returns, so
    /// <c>GetBlock</c>/<c>GetBlockId</c> called during generation return the pre-commit (air) value
    /// for positions this call already wrote. Use <c>GetStagedBlockId</c> (or set
    /// <c>ReadFromStagedByDefault</c>) to read back your own writes.
    /// </summary>
    IBlockAccessor BlockAccessor { get; }

    /// <summary>
    /// Random generator constructed with seed <c>WorldSeed ^ (ChunkX * 1299721) ^ (ChunkZ * 1000033)</c>.
    /// The sequence does not depend on <see cref="DimensionId"/>, so two dimensions using the same
    /// strategy get identical sequences for the same chunk coordinates. Because of how
    /// <c>LCGRandom</c>'s constructor derives its first internal state, the very first value drawn
    /// from a freshly constructed instance is also the same regardless of the seed; only draws after
    /// the first vary with chunk coordinates and world seed.
    /// </summary>
    LCGRandom Rng { get; }
}
