using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Manifold.Api.Worldgen;
using Manifold.Internal.Util;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Active worldgen driver. Generates or loads chunk columns on demand by directly calling the
/// engine's <c>CreateChunkColumnForDimension</c> / <c>LoadChunkColumnForDimension</c> APIs; no
/// Harmony patches or event hooks are involved.
/// </summary>
/// <remarks>
/// Server-side, main thread. Call <see cref="EnsureRegion"/> whenever a player enters a custom
/// dimension; the generator creates missing columns, fills them via the registered
/// <see cref="IWorldgenStrategy"/>, and pushes them to the player (no server relight; see
/// <see cref="EnsureRegion"/>).
/// </remarks>
internal sealed class DimensionGenerator
{
    /// <summary>Consecutive strategy failures before auto-disabling a dimension's worldgen.</summary>
    internal const int MaxConsecutiveFailures = 4;

    private readonly DimensionRegistry _registry;
    private readonly GeneratedColumnStore _generatedColumns;
    private readonly HashSet<int> _initialized = new();
    private readonly ConcurrentDictionary<int, int> _failureCounts = new();
    private readonly HashSet<int> _disabled = new();

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
    public bool IsDisabled(int dimId) => _disabled.Contains(dimId);

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
        _initialized.Remove(dimId);
        _failureCounts.TryRemove(dimId, out _);
        _disabled.Remove(dimId);
    }

    /// <summary>
    /// Returns the consecutive failure count for the given dimension. No production caller; kept as
    /// a deliberate test seam for asserting <see cref="RecordFailure"/>'s counting/auto-disable logic.
    /// </summary>
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
        if (!TryPrepare(sapi, dimId, out var dim, out var strategy))
        {
            return;
        }

        FillRegionColumns(sapi, dimId, centerCx, centerCz, dim.GenerationRadius, strategy, player);

        // No server relight: a server FullRelight floods skylight into custom dims and overrides
        // the client's own lighting (which floods sunlight and relights edits on receipt natively).
        // Consumers relight explicitly via IManifoldServer.RelightRegion / the /manifold relight command.
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
    /// <returns><c>true</c> if the column was newly generated; <c>false</c> if loaded or skipped.</returns>
    public bool EnsureColumn(ICoreServerAPI sapi, int dimId, int cx, int cz)
    {
        if (cx < 0 || cz < 0 || !TryPrepare(sapi, dimId, out _, out var strategy))
        {
            return false;
        }

        return GenerateOrLoadColumn(sapi, dimId, cx, cz, strategy);
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

    /// <summary>
    /// Shared guard for <see cref="EnsureRegion"/> and <see cref="EnsureColumn"/>: resolves the
    /// dimension's worldgen strategy and ensures <c>OnInitialize</c> has run successfully for it.
    /// On an init failure, removes the initialised marker so the next visit retries instead of
    /// generating uninitialised terrain (block ids unresolved); the failure still counts toward
    /// auto-disable via <see cref="RecordFailure"/> inside <see cref="InvokeInitialize"/>.
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="dim">The resolved dimension, if ready to generate.</param>
    /// <param name="strategy">The dimension's worldgen strategy, if ready to generate.</param>
    /// <returns><c>true</c> if the caller may proceed to generate/load columns.</returns>
    private bool TryPrepare(
        ICoreServerAPI? sapi, int dimId, out DimensionImpl dim, out IWorldgenStrategy strategy)
    {
        dim = null!;
        strategy = null!;

        // Overworld is handled natively; never drive it ourselves.
        if (sapi is null || dimId == 0 || IsDisabled(dimId))
        {
            return false;
        }

        var found = _registry.GetByInternalId(dimId);
        if (found?.Worldgen is not { } foundStrategy)
        {
            return false;
        }

        dim = found;
        strategy = foundStrategy;

        if (_initialized.Add(dimId) && !InvokeInitialize(strategy, sapi, dimId))
        {
            _initialized.Remove(dimId);
            return false;
        }

        return true;
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

        // First-ever visit: allocate empty chunk slots, populate via strategy. No server relight:
        // the client lights freshly-received columns itself; see EnsureRegion.
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

        // Raised after the commit above, before the caller sends the column to any client (both
        // FillRegionColumns and the streaming driver send afterwards), so a handler's own writes to
        // this same column reach clients as part of its normal first send.
        //
        // The accessor handed to subscribers uses the same synchronize:false, relight:false
        // semantics as the strategy's own bulk accessor above (not sapi.World.BlockAccessor
        // / ServerMain.BlockAccessor / WorldMap.RelaxedBlockAccess), which is built with
        // synchronize:true, relight:true and would make decoration behave like a live player edit:
        // a queued server relight task (the flood EnsureRegion deliberately avoids) and a
        // ModifiedBlocks/neighbour-update entry that breaks unsupported loose blocks and starts
        // fluids flowing.
        if (_registry.GetByInternalId(dimId) is { } dim)
        {
            IBlockAccessor postProcessAccessor =
                sapi.World.GetBlockAccessor(synchronize: false, relight: false, strict: false);
            _registry.RaiseColumnGenerated(dim, cx, cz, postProcessAccessor);
        }

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

        int baseX = cx * ChunkMath.ChunkSize;
        int baseZ = cz * ChunkMath.ChunkSize;
        var pos = new BlockPos(baseX, capY, baseZ, dimId);
        for (int lx = 0; lx < ChunkMath.ChunkSize; lx++)
        {
            for (int lz = 0; lz < ChunkMath.ChunkSize; lz++)
            {
                pos.Set(baseX + lx, capY, baseZ + lz);
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
        if (count >= MaxConsecutiveFailures && _disabled.Add(dimId))
        {
            StrategyAutoDisabled?.Invoke(dimId);
        }
    }
}
