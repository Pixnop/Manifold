using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Worldgen;

/// <summary>
/// Provided to <see cref="IWorldgenStrategy.OnChunkColumnGen"/> for one chunk column per pass.
/// </summary>
/// <remarks>
/// Server-side, worldgen worker thread.
/// <see cref="BlockAccessor"/> is per-worker and thread-safe; never replace it with <c>api.World.BlockAccessor</c>.
/// </remarks>
public interface IWorldgenChunkContext
{
    /// <summary>Engine dimension id of the chunk being generated.</summary>
    int DimensionId { get; }

    /// <summary>Chunk X index (multiply by 32 for world coordinates).</summary>
    int ChunkX { get; }

    /// <summary>Chunk Z index.</summary>
    int ChunkZ { get; }

    /// <summary>Chunks of this column (index 0 = lowest).</summary>
    IServerChunk[] Chunks { get; }

    /// <summary>Dimension-aware, thread-safe block accessor bound to this worker.</summary>
    IWorldGenBlockAccessor BlockAccessor { get; }

    /// <summary>Per-worker random generator initialised from <see cref="IWorldgenInitContext.Seed"/>.</summary>
    LCGRandom Rng { get; }
}
