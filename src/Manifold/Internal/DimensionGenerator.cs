using System;
using System.Collections.Concurrent;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Active worldgen driver. Replaces the old <c>ChunkColumnGeneration</c>-hook approach.
/// Generates or loads chunk columns on demand by directly calling the engine's
/// <c>CreateChunkColumnForDimension</c> / <c>LoadChunkColumnForDimension</c> APIs.
/// </summary>
/// <remarks>
/// Server-side, main thread. Call <see cref="EnsureRegion"/> whenever a player enters a custom
/// dimension; the generator creates missing columns, fills them via the registered
/// <see cref="IWorldgenStrategy"/>, relights them, and pushes them to the player.
/// </remarks>
internal sealed class DimensionGenerator
{
    private const int MaxConsecutiveFailures = 4;

    /// <summary>
    /// Upper Y bound for the single post-generation relight pass. Relighting the full map height
    /// per column is what made generation take ~40s; bounding it to a low band keeps a flat/void
    /// floor correctly lit while staying cheap. Strategies that build above this band will be
    /// under-lit until VS naturally relights (acceptable for v0; configurable later).
    /// </summary>
    private const int RelightMaxY = 64;

    private readonly DimensionRegistry _registry;
    private readonly ConcurrentDictionary<int, bool> _initialized = new();
    private readonly ConcurrentDictionary<int, int> _failureCounts = new();
    private readonly ConcurrentDictionary<int, bool> _disabled = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DimensionGenerator"/> class.
    /// </summary>
    /// <param name="registry">Dimension registry used to look up strategies.</param>
    public DimensionGenerator(DimensionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Raised when a strategy throws during generation.</summary>
    public event Action<int, IWorldgenStrategy, Exception>? StrategyThrew;

    /// <summary>Raised when a strategy is auto-disabled after too many consecutive failures.</summary>
    public event Action<int>? StrategyAutoDisabled;

    /// <summary>Returns <c>true</c> if the strategy for the given dimension has been auto-disabled.</summary>
    /// <param name="dimId">Engine dimension id.</param>
    /// <returns><c>true</c> if disabled.</returns>
    public bool IsDisabled(int dimId) => _disabled.TryGetValue(dimId, out var d) && d;

    /// <summary>Returns the consecutive failure count for the given dimension.</summary>
    /// <param name="dimId">Engine dimension id.</param>
    /// <returns>Count of consecutive failures since last success.</returns>
    public int GetConsecutiveFailureCount(int dimId) =>
        _failureCounts.TryGetValue(dimId, out var n) ? n : 0;

    /// <summary>
    /// Ensures that all chunk columns in a radius around <paramref name="centerCx"/>, <paramref name="centerCz"/>
    /// are generated or loaded for <paramref name="dimId"/>, then pushed to <paramref name="player"/> if non-null.
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="dimId">Engine dimension id to generate for.</param>
    /// <param name="centerCx">Center chunk X.</param>
    /// <param name="centerCz">Center chunk Z.</param>
    /// <param name="player">Player to push chunks to, or <c>null</c> to skip force-send.</param>
    public void EnsureRegion(ICoreServerAPI sapi, int dimId, int centerCx, int centerCz, IServerPlayer? player)
    {
        if (sapi is null)
        {
            return;
        }

        // Overworld is handled natively; never drive it ourselves.
        if (dimId == 0)
        {
            return;
        }

        if (IsDisabled(dimId))
        {
            return;
        }

        var dim = _registry.GetByInternalId(dimId);
        if (dim?.Worldgen is not { } strategy)
        {
            return;
        }

        int radius = dim.GenerationRadius;

        // Ensure OnInitialize is called once per dimension.
        if (_initialized.TryAdd(dimId, true))
        {
            bool initOk = InvokeInitialize(strategy, sapi, dimId);
            if (!initOk)
            {
                // init failure counts toward auto-disable
                return;
            }
        }

        bool anyGenerated = false;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                int cx = centerCx + dx;
                int cz = centerCz + dz;

                if (cx < 0 || cz < 0)
                {
                    continue;
                }

                anyGenerated |= GenerateOrLoadColumn(sapi, dimId, cx, cz, strategy);

                if (player != null)
                {
                    sapi.WorldManager.ForceSendChunkColumn(player, cx, cz, dimId);
                }
            }
        }

        // Relight ONCE for the whole freshly-generated region, over a bounded Y band.
        // (Per-column full-height relight was the ~40s bottleneck.)
        if (anyGenerated)
        {
            int minX = (centerCx - radius) * 32;
            int minZ = (centerCz - radius) * 32;
            int maxX = ((centerCx + radius) * 32) + 31;
            int maxZ = ((centerCz + radius) * 32) + 31;
            int maxY = Math.Min(RelightMaxY, sapi.WorldManager.MapSizeY - 1);
            try
            {
                sapi.WorldManager.FullRelight(new BlockPos(minX, 0, minZ), new BlockPos(maxX, maxY, maxZ), false);
            }
            catch
            {
                // FullRelight is best-effort; don't block transit if it fails.
            }
        }
    }

    /// <summary>
    /// Testable seam: invokes <see cref="IWorldgenStrategy.GenerateColumn"/> for the given context,
    /// handling exceptions and updating the failure / auto-disable state.
    /// </summary>
    /// <param name="strategy">Strategy to invoke.</param>
    /// <param name="ctx">Per-column context.</param>
    /// <param name="dimId">Engine dimension id (used for failure tracking).</param>
    public void InvokeStrategyColumn(IWorldgenStrategy strategy, IWorldgenChunkContext ctx, int dimId)
    {
        if (IsDisabled(dimId))
        {
            return;
        }

        try
        {
            strategy.GenerateColumn(ctx);
            _failureCounts[dimId] = 0;
        }
        catch (Exception ex)
        {
            RecordFailure(strategy, dimId, ex);
        }
    }

    private bool InvokeInitialize(IWorldgenStrategy strategy, ICoreServerAPI sapi, int dimId)
    {
        try
        {
            var ctx = new WorldgenInitContext(dimId, sapi.WorldManager.Seed, sapi);
            strategy.OnInitialize(ctx);
            return true;
        }
        catch (Exception ex)
        {
            RecordFailure(strategy, dimId, ex);
            return false;
        }
    }

    /// <summary>Generates or loads a single column. Returns <c>true</c> if it was newly generated.</summary>
    private bool GenerateOrLoadColumn(ICoreServerAPI sapi, int dimId, int cx, int cz, IWorldgenStrategy strategy)
    {
        // Determine whether this column already exists on disk / in loaded chunks.
        // Per Appendix D: the chunk Y index for the bottom of a dimension's column is dim * 1024
        // (derived from CreateChunkColumnForDimension: cy = dim * 32768 / 32 = dim * 1024).
        // If GetChunk returns non-null the column was already created/loaded; use Load path.
        int cyBase = dimId * 1024;
        bool exists = sapi.WorldManager.GetChunk(cx, cyBase, cz) != null;

        if (exists)
        {
            // Chunks already in loaded-chunks dictionary; re-enqueue a load to ensure persistence
            // is up to date on subsequent server restarts. No relight needed.
            sapi.WorldManager.LoadChunkColumnForDimension(cx, cz, dimId);
            return false;
        }

        // First visit: allocate empty chunk slots, populate via strategy. Relight is done ONCE
        // for the whole region by the caller (per-column relight was the ~40s bottleneck).
        sapi.WorldManager.CreateChunkColumnForDimension(cx, cz, dimId);

        // Bulk accessor batches all SetBlock writes into a single Commit.
        // synchronize:false because we ForceSendChunkColumn explicitly afterward.
        IBulkBlockAccessor accessor = sapi.World.GetBlockAccessorBulkUpdate(synchronize: false, relight: false);

        // Seed RNG from world seed XOR a position-derived value for determinism.
        int rngSeed = sapi.WorldManager.Seed ^ (cx * 1299721) ^ (cz * 1000033);
        var rng = new LCGRandom(rngSeed);
        var ctx = new WorldgenChunkContext(dimId, cx, cz, accessor, rng);

        InvokeStrategyColumn(strategy, ctx, dimId);
        accessor.Commit();
        return true;
    }

    private void RecordFailure(IWorldgenStrategy strategy, int dimId, Exception ex)
    {
        int count = _failureCounts.AddOrUpdate(dimId, 1, (_, prev) => prev + 1);
        StrategyThrew?.Invoke(dimId, strategy, ex);
        if (count >= MaxConsecutiveFailures && !IsDisabled(dimId))
        {
            _disabled[dimId] = true;
            StrategyAutoDisabled?.Invoke(dimId);
        }
    }
}
