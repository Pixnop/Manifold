using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>Concrete <see cref="IWorldgenChunkContext"/>.</summary>
internal sealed class WorldgenChunkContext : IWorldgenChunkContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldgenChunkContext"/> class.
    /// </summary>
    /// <param name="dimensionId">Engine dimension id.</param>
    /// <param name="chunkX">Chunk X index.</param>
    /// <param name="chunkZ">Chunk Z index.</param>
    /// <param name="blockAccessor">Block accessor for writing terrain.</param>
    /// <param name="rng">Deterministic per-column random generator.</param>
    public WorldgenChunkContext(int dimensionId, int chunkX, int chunkZ, IBlockAccessor blockAccessor, LCGRandom rng)
    {
        DimensionId = dimensionId;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        BlockAccessor = blockAccessor;
        Rng = rng;
    }

    /// <inheritdoc/>
    public int DimensionId { get; }

    /// <inheritdoc/>
    public int ChunkX { get; }

    /// <inheritdoc/>
    public int ChunkZ { get; }

    /// <inheritdoc/>
    public IBlockAccessor BlockAccessor { get; }

    /// <inheritdoc/>
    public LCGRandom Rng { get; }
}
