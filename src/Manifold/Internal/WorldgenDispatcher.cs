using System;
using System.Collections.Concurrent;
using Manifold.Api.Worldgen;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Single registration point for worldgen handlers; dispatches by current dimension id.
/// </summary>
/// <remarks>
/// Server-side. <see cref="Bind"/> / <see cref="Unbind"/> on main thread.
/// <see cref="Dispatch"/> runs on worldgen worker threads; thread-safe via <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </remarks>
internal sealed class WorldgenDispatcher
{
    private const int MaxConsecutiveFailures = 4;

    private readonly ConcurrentDictionary<int, IWorldgenStrategy> _strategies = new();
    private readonly ConcurrentDictionary<int, int> _failureCounts = new();
    private readonly ConcurrentDictionary<int, bool> _disabled = new();

    /// <summary>Raised when a strategy throws during <see cref="Dispatch"/>.</summary>
    public event Action<int, IWorldgenStrategy, Exception>? StrategyThrew;

    /// <summary>Raised when a strategy is auto-disabled after too many consecutive failures.</summary>
    public event Action<int>? StrategyAutoDisabled;

    /// <summary>Register a strategy for a dimension id.</summary>
    /// <param name="internalId">Engine dimension id.</param>
    /// <param name="strategy">Strategy to invoke for that dim.</param>
    public void Bind(int internalId, IWorldgenStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategies[internalId] = strategy;
        _failureCounts[internalId] = 0;
        _disabled.TryRemove(internalId, out _);
    }

    /// <summary>Unregister a strategy and clear its failure state.</summary>
    /// <param name="internalId">Engine dimension id.</param>
    public void Unbind(int internalId)
    {
        _strategies.TryRemove(internalId, out _);
        _failureCounts.TryRemove(internalId, out _);
        _disabled.TryRemove(internalId, out _);
    }

    /// <summary>True if the strategy for the given dim is currently auto-disabled.</summary>
    /// <param name="internalId">Engine dimension id.</param>
    /// <returns><c>true</c> if disabled.</returns>
    public bool IsDisabled(int internalId) => _disabled.TryGetValue(internalId, out var d) && d;

    /// <summary>Read the consecutive-failure counter for a dim.</summary>
    /// <param name="internalId">Engine dimension id.</param>
    /// <returns>Count of consecutive failures since last success.</returns>
    public int GetConsecutiveFailureCount(int internalId) =>
        _failureCounts.TryGetValue(internalId, out var n) ? n : 0;

    /// <summary>Dispatch a chunk-gen call to the bound strategy.</summary>
    /// <param name="ctx">Per-chunk context.</param>
    /// <param name="pass">Worldgen pass.</param>
    public void Dispatch(IWorldgenChunkContext ctx, EnumWorldGenPass pass)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!_strategies.TryGetValue(ctx.DimensionId, out var strategy))
        {
            return;
        }

        if (_disabled.TryGetValue(ctx.DimensionId, out var off) && off)
        {
            return;
        }

        if (!strategy.Passes.Contains(pass))
        {
            return;
        }

        try
        {
            strategy.OnChunkColumnGen(ctx, pass);
            _failureCounts[ctx.DimensionId] = 0;
        }
        catch (Exception ex)
        {
            int count = _failureCounts.AddOrUpdate(ctx.DimensionId, 1, (_, prev) => prev + 1);
            StrategyThrew?.Invoke(ctx.DimensionId, strategy, ex);
            if (count >= MaxConsecutiveFailures)
            {
                _disabled[ctx.DimensionId] = true;
                StrategyAutoDisabled?.Invoke(ctx.DimensionId);
            }
        }
    }

    /// <summary>
    /// Hook Manifold's dispatcher into VS's ChunkColumnGeneration events.
    /// </summary>
    /// <remarks>
    /// Phase 11 will wire the chunk-context factory; for now this method exists so other
    /// services can call it but does no actual hooking. The full body lands when
    /// <c>WorldgenChunkContextFactory</c> exists.
    /// </remarks>
    /// <param name="sapi">Server API.</param>
    internal void RegisterServerHook(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);

        // Phase 11: wire api.Event.GetWorldgenBlockAccessor + api.Event.ChunkColumnGeneration via WorldgenChunkContextFactory.
        // For now, a no-op so consumers can call it during boot wiring.
    }
}
