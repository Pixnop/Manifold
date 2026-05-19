using System;
using Manifold.Api.Worldgen;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Wraps an engine <see cref="IChunkColumnGenerateRequest"/> into Manifold's
/// <see cref="IWorldgenChunkContext"/>. Per-worker accessor + RNG.
/// </summary>
/// <remarks>Server-side. Each worldgen worker thread binds its own accessor via <see cref="BindWorker"/>.</remarks>
internal sealed class WorldgenChunkContextFactory
{
    private readonly int _seed;

    [ThreadStatic]
    private static IWorldGenBlockAccessor? _wgAccessor;

    [ThreadStatic]
    private static LCGRandom? _rng;

    /// <summary>Initializes a new instance of the <see cref="WorldgenChunkContextFactory"/> class.</summary>
    /// <param name="seed">World seed used to initialise per-worker RNGs.</param>
    public WorldgenChunkContextFactory(int seed) => _seed = seed;

    /// <summary>Wrap a chunk request into a context. Returns <c>null</c> if the worker hasn't been bound yet.</summary>
    /// <param name="request">Engine request.</param>
    /// <returns>Wrapped context, or <c>null</c>.</returns>
    public IWorldgenChunkContext? Create(IChunkColumnGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_wgAccessor is null || _rng is null)
        {
            return null;
        }

        int dim = WorldgenDispatcher.ResolveDimensionFromRequest(request);
        return new WorldgenChunkContext(dim, request, _wgAccessor, _rng);
    }

    /// <summary>Bind the per-worker thread-local accessor (called from <c>GetWorldgenBlockAccessor</c> hook).</summary>
    /// <param name="accessor">Per-worker block accessor.</param>
    internal void BindWorker(IWorldGenBlockAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _wgAccessor = accessor;
        _rng = new LCGRandom(_seed);
    }

    private sealed class WorldgenChunkContext : IWorldgenChunkContext
    {
        private readonly IChunkColumnGenerateRequest _request;

        public WorldgenChunkContext(
            int dim,
            IChunkColumnGenerateRequest request,
            IWorldGenBlockAccessor accessor,
            LCGRandom rng)
        {
            DimensionId = dim;
            _request = request;
            BlockAccessor = accessor;
            Rng = rng;
        }

        public int DimensionId { get; }

        public int ChunkX => _request.ChunkX;

        public int ChunkZ => _request.ChunkZ;

        public IServerChunk[] Chunks => _request.Chunks;

        public IWorldGenBlockAccessor BlockAccessor { get; }

        public LCGRandom Rng { get; }
    }
}
