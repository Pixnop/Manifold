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

    private readonly DimensionRegistry _registry;
    private readonly GeneratedColumnStore _generatedColumns;
    private readonly ConcurrentDictionary<int, bool> _initialized = new();
    private readonly ConcurrentDictionary<int, int> _failureCounts = new();
    private readonly ConcurrentDictionary<int, bool> _disabled = new();

    /// <summary>
    /// Candidate opaque block codes for the dark-sky ceiling cap, tried in order. The first that
    /// resolves and fully blocks light (LightAbsorption &gt; 32) is used. Solid rock is always opaque.
    /// </summary>
    private static readonly string[] CapBlockCandidates =
    {
        "game:rock-granite", "game:rock-andesite", "game:rock-basalt", "game:rock-sandstone",
    };

    // Resolved cap block id (LightAbsorption-validated), cached after the first lookup. -1 = not yet
    // resolved, 0 = no suitable block found (dark-sky becomes a no-op, logged once).
    private int _capBlockId = -1;

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

    /// <summary>
    /// Drops all per-dimension generator state (initialised flag, failure count, auto-disabled flag)
    /// for <paramref name="dimId"/>. Must be called when a dimension is destroyed: the engine id is
    /// released back to the allocator and may be reused by a later dimension, which would otherwise
    /// inherit this one's stale flags (e.g. a reused id born permanently auto-disabled, or skipping
    /// <c>OnInitialize</c>). With ephemeral reap-on-empty, id reuse within a session is routine.
    /// </summary>
    /// <param name="dimId">Engine dimension id being released.</param>
    public void ForgetDimension(int dimId)
    {
        _initialized.TryRemove(dimId, out _);
        _failureCounts.TryRemove(dimId, out _);
        _disabled.TryRemove(dimId, out _);
    }

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

        // Ensure OnInitialize runs SUCCESSFULLY once per dimension. On failure, remove the marker so the
        // next visit retries init rather than generating uninitialized terrain (block ids unresolved).
        // The failure still counts toward auto-disable via RecordFailure inside InvokeInitialize.
        if (_initialized.TryAdd(dimId, true) && !InvokeInitialize(strategy, sapi, dimId))
        {
            _initialized.TryRemove(dimId, out _);
            return;
        }

        FillRegionColumns(sapi, dimId, centerCx, centerCz, radius, strategy, player);

        // No automatic server-side relight here. The client lights freshly-received chunk columns
        // natively (sunlight flood on receipt + incremental relight on block edits), exactly as it
        // did in every released version - where this relight was a dim-0 no-op and custom-dim
        // lighting was correct. Forcing a server FullRelight on the custom dim (#60) floods skylight
        // and is force-sent by the streaming driver, overriding the correct client lighting (dims
        // full-bright, torches/edits not relighting). Modders who need an explicit relight after a
        // runtime edit use IManifoldServer.RelightRegion / the /manifold relight command.
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
            // Retry init on the next visit instead of generating uninitialized terrain.
            _initialized.TryRemove(dimId, out _);
            return false;
        }

        return GenerateOrLoadColumn(sapi, dimId, cx, cz, strategy);
    }

    /// <summary>
    /// Dim-aware wrapper over the engine's <c>FullRelight</c>: relights the block bounds in the
    /// given dimension. The dimension field of both positions is overwritten with
    /// <paramref name="dimId"/> so callers cannot accidentally relight the overworld (which is
    /// exactly the bug this guards against - a <c>BlockPos</c> built without a dimension targets
    /// dim 0). Best-effort: lighting failures never propagate.
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="dimId">Engine dimension id to relight in.</param>
    /// <param name="min">Minimum corner (local coordinates).</param>
    /// <param name="max">Maximum corner (local coordinates).</param>
    /// <param name="sendToClients">
    /// When <c>true</c>, the recomputed light is pushed to clients immediately. Required for
    /// runtime relights of chunks already loaded on the client (e.g. the relight command, or
    /// after a runtime block placement) - otherwise the server light is correct but the client
    /// never re-meshes and the change is invisible. Worldgen passes <c>false</c> because the
    /// freshly generated column is sent to the client separately (on transit / by the streaming
    /// driver).
    /// </param>
    public static void RelightBlockBounds(ICoreServerAPI sapi, int dimId, BlockPos min, BlockPos max, bool sendToClients)
    {
        var minPos = new BlockPos(min.X, min.Y, min.Z, dimId);
        var maxPos = new BlockPos(max.X, max.Y, max.Z, dimId);
        try
        {
            sapi.WorldManager.FullRelight(minPos, maxPos, sendToClients);
        }
        catch
        {
            // FullRelight is best-effort; never block on a lighting failure.
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

    /// <summary>Iterates all chunk columns in the radius square, generating or loading each and
    /// force-sending it to the player. No return value: there is no automatic relight to drive.</summary>
    private void FillRegionColumns(
        ICoreServerAPI sapi,
        int dimId,
        int centerCx,
        int centerCz,
        int radius,
        IWorldgenStrategy strategy,
        IServerPlayer? player)
    {
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

                GenerateOrLoadColumn(sapi, dimId, cx, cz, strategy);

                if (player != null)
                {
                    sapi.WorldManager.ForceSendChunkColumn(player, cx, cz, dimId);
                }
            }
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
        PlaceSkyCapIfConfigured(sapi, dimId, cx, cz, accessor);
        accessor.Commit();
        _generatedColumns.MarkGenerated(dimId, cx, cz);
        return true;
    }

    /// <summary>
    /// For dark-sky dimensions (<c>WithDarkSky</c>), seals this freshly-generated column with an
    /// opaque ceiling layer at the configured Y across the full 32x32 footprint, on the same bulk
    /// accessor (committed by the caller). The cap stops the engine's top-down skylight flood so the
    /// dimension stays dark below it, and makes the chunk non-empty so the client does not bleed
    /// full-bright skylight from neighbouring empty chunks. No-op when the dimension has no cap.
    /// </summary>
    private void PlaceSkyCapIfConfigured(ICoreServerAPI sapi, int dimId, int cx, int cz, IBulkBlockAccessor accessor)
    {
        var dim = _registry.GetByInternalId(dimId);
        if (dim?.SkyCapY is not { } capY)
        {
            return;
        }

        int capBlockId = ResolveCapBlock(sapi);
        if (capBlockId <= 0)
        {
            return;
        }

        int baseX = cx * 32;
        int baseZ = cz * 32;
        var pos = new BlockPos(baseX, capY, baseZ, dimId);
        for (int lx = 0; lx < 32; lx++)
        {
            for (int lz = 0; lz < 32; lz++)
            {
                pos.Set(baseX + lx, capY, baseZ + lz);
                pos.dimension = dimId;
                accessor.SetBlock(capBlockId, pos);
            }
        }
    }

    /// <summary>
    /// Resolves (and caches) an opaque block for the dark-sky cap. Picks the first candidate that
    /// resolves and fully blocks light (<c>LightAbsorption &gt; 32</c>). Returns 0 and logs once if
    /// none is suitable, in which case dark-sky silently does nothing rather than capping with a
    /// translucent block.
    /// </summary>
    private int ResolveCapBlock(ICoreServerAPI sapi)
    {
        if (_capBlockId >= 0)
        {
            return _capBlockId;
        }

        foreach (var code in CapBlockCandidates)
        {
            var block = sapi.World.GetBlock(new AssetLocation(code));
            if (block is not null && block.Id != 0 && block.LightAbsorption > 32)
            {
                _capBlockId = block.Id;
                return _capBlockId;
            }
        }

        _capBlockId = 0;
        sapi.Logger.Warning(
            "[Manifold] WithDarkSky: no fully-opaque cap block resolved (tried {0}); dark-sky has no effect.",
            string.Join(", ", CapBlockCandidates));
        return 0;
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
