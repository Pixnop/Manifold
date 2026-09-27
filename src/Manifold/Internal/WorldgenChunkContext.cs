using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>Concrete <see cref="IWorldgenChunkContext"/>.</summary>
/// <param name="DimensionId">Engine dimension id.</param>
/// <param name="ChunkX">Chunk X index.</param>
/// <param name="ChunkZ">Chunk Z index.</param>
/// <param name="BlockAccessor">Block accessor for writing terrain.</param>
/// <param name="Rng">Deterministic per-column random generator.</param>
internal sealed record WorldgenChunkContext(
    int DimensionId, int ChunkX, int ChunkZ, IBlockAccessor BlockAccessor, LCGRandom Rng) : IWorldgenChunkContext;
