using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// Upper Y bound for the relight pass. FullRelight cost scales with this height, and for
    /// streaming we relight per tick, so a tight band is critical: profiling showed Y0-64 cost
    /// ~180ms per 4-column tick (overloading the server), dominated entirely by relight. A low
    /// band keeps a flat/void floor correctly lit while staying cheap. Strategies that build above
    /// this band are under-lit until VS naturally relights (acceptable for v1; configurable later).
    /// </summary>
    private const int RelightMaxY = 20;

    private readonly DimensionRegistry _registry;
    private readonly GeneratedColumnStore _generatedColumns;
    private readonly ConcurrentDictionary<int, bool> _initialized = new();
    private readonly ConcurrentDictionary<int, int> _failureCounts = new();
    private readonly ConcurrentDictionary<int, bool> _disabled = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DimensionGenerator"/> class.
    /// </summary>
    /// <param name="registry">Dimension registry used to look up strategies.</param>
    /// <param name="generatedColumns">Persisted set of already-generated columns (load vs regenerate decision).</param>
    public DimensionGenerator(DimensionRegistry registry, GeneratedColumnStore generatedColumns)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _generatedColumns = generatedColumns ?? throw new ArgumentNullException(nameof(generatedColumns));
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

        bool anyGenerated = FillRegionColumns(sapi, dimId, centerCx, centerCz, radius, strategy, player);

        // Relight ONCE for the whole freshly-generated region, over a bounded Y band.
        // (Per-column full-height relight was the ~40s bottleneck.)
        if (anyGenerated)
        {
            RelightChunkBounds(sapi, centerCx - radius, centerCz - radius, centerCx + radius, centerCz + radius);
        }
    }

    /// <summary>
    /// Ensures a single chunk column is generated or loaded for a streaming dimension. Runs the
    /// same guards and initialisation as <see cref="EnsureRegion"/>. Does NOT relight or force-send
    /// (the streaming driver batches those). Returns <c>true</c> if the column was newly generated.
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="cx">Chunk X.</param>
    /// <param name="cz">Chunk Z.</param>
    /// <returns><c>true</c> if newly generated (caller should relight).</returns>
    public bool EnsureColumn(ICoreServerAPI sapi, int dimId, int cx, int cz)
    {
        if (sapi is null || dimId == 0 || IsDisabled(dimId) || cx < 0 || cz < 0)
        {
            return false;
        }

        var dim = _registry.GetByInternalId(dimId);
        if (dim?.Worldgen is not { } strategy)
        {
            return false;
        }

        if (_initialized.TryAdd(dimId, true) && !InvokeInitialize(strategy, sapi, dimId))
        {
            return false;
        }

        return GenerateOrLoadColumn(sapi, dimId, cx, cz, strategy);
    }

    /// <summary>
    /// Relights newly-generated columns (best-effort). Each column is relit over its own 32x32
    /// footprint rather than the bounding box of the whole batch: profiling showed that relighting
    /// the spanning rectangle of spread-out columns (common when a fast-moving player generates a
    /// line of columns) re-lights many already-lit columns in between, and FullRelight cost scales
    /// with that area. Per-column relight makes the cost proportional to the number of new columns,
    /// independent of how spread out they are.
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="columns">Newly-generated columns to relight.</param>
    public static void RelightColumns(ICoreServerAPI sapi, IReadOnlyList<(int Cx, int Cz)> columns)
    {
        if (sapi is null || columns is null)
        {
            return;
        }

        foreach (var (cx, cz) in columns)
        {
            RelightChunkBounds(sapi, cx, cz, cx, cz);
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

    /// <summary>Iterates all chunk columns in the radius square, generating or loading each.
    /// Returns <c>true</c> if at least one column was newly generated.</summary>
    private bool FillRegionColumns(
        ICoreServerAPI sapi,
        int dimId,
        int centerCx,
        int centerCz,
        int radius,
        IWorldgenStrategy strategy,
        IServerPlayer? player)
    {
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

        return anyGenerated;
    }

    /// <summary>Relights a rectangle of chunk columns over a bounded Y band (best-effort).</summary>
    private static void RelightChunkBounds(ICoreServerAPI sapi, int minCx, int minCz, int maxCx, int maxCz)
    {
        int minX = minCx * 32;
        int minZ = minCz * 32;
        int maxX = (maxCx * 32) + 31;
        int maxZ = (maxCz * 32) + 31;
        int maxY = Math.Min(RelightMaxY, sapi.WorldManager.MapSizeY - 1);
        try
        {
            sapi.WorldManager.FullRelight(new BlockPos(minX, 0, minZ), new BlockPos(maxX, maxY, maxZ), false);
        }
        catch
        {
            // FullRelight is best-effort; never block on a lighting failure.
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
        // Decide load-vs-generate using Manifold's persisted generated-set, NOT GetChunk:
        // GetChunk is in-memory only, so after a server restart it reports "not loaded" for a
        // column that exists on disk, which would trigger a regenerate that overwrites player
        // modifications. The engine has no synchronous on-disk existence check.
        if (_generatedColumns.IsGenerated(dimId, cx, cz))
        {
            // Previously generated and persisted; restore from disk. No relight needed.
            sapi.WorldManager.LoadChunkColumnForDimension(cx, cz, dimId);
            return false;
        }

        // First-ever visit: allocate empty chunk slots, populate via strategy. Relight is done ONCE
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
        _generatedColumns.MarkGenerated(dimId, cx, cz);
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
