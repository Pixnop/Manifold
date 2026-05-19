using System;
using System.Collections.Concurrent;
using System.Reflection;
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
    /// Hook Manifold's dispatcher into VS's <c>ChunkColumnGeneration</c> events for all passes.
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="factory">Per-worker chunk-context factory.</param>
    internal void RegisterServerHook(ICoreServerAPI sapi, WorldgenChunkContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        ArgumentNullException.ThrowIfNull(factory);

        sapi.Event.GetWorldgenBlockAccessor(provider =>
            factory.BindWorker(provider.GetBlockAccessor(true)));

        var passes = new[]
        {
            EnumWorldGenPass.Terrain,
            EnumWorldGenPass.TerrainFeatures,
            EnumWorldGenPass.Vegetation,
            EnumWorldGenPass.PreDone,
        };

        foreach (var pass in passes)
        {
            EnumWorldGenPass passLocal = pass; // closure-safe capture
            sapi.Event.ChunkColumnGeneration(
                request =>
                {
                    var ctx = factory.Create(request);
                    if (ctx is null || ctx.DimensionId == 0)
                    {
                        return;
                    }

                    Dispatch(ctx, passLocal);
                },
                pass,
                "standard");
        }
    }

    /// <summary>
    /// Reads the current dimension id from a chunk-generation request via reflection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FRAGILE — <c>IChunkColumnGenerateRequest</c> doesn't expose <c>dimension</c> on its public
    /// interface, but the engine's concrete impl (<c>ChunkColumnLoadRequest</c>) has a
    /// <c>dimension</c> field/property. This helper reflects on the runtime type at first call
    /// and caches the <see cref="MemberInfo"/>.
    /// </para>
    /// <para>
    /// If VS renames or removes the member in a future version, the method returns <c>0</c> (overworld);
    /// Phase 12 smoke testing must verify cross-dim chunks still generate correctly after each VS bump.
    /// </para>
    /// </remarks>
    /// <param name="request">Chunk-generation request from <c>api.Event.ChunkColumnGeneration</c>.</param>
    /// <returns>Dimension id, or <c>0</c> if the member can't be located.</returns>
    internal static int ResolveDimensionFromRequest(IChunkColumnGenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DimensionAccessor.Read(request);
    }

    /// <summary>
    /// Per-request-type cached reflection accessor for the <c>dimension</c> member.
    /// </summary>
    private static class DimensionAccessor
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static Type? _cachedType;
        private static Func<object, int>? _cachedReader;

        public static int Read(IChunkColumnGenerateRequest request)
        {
            var type = request.GetType();
            if (!ReferenceEquals(type, _cachedType))
            {
                _cachedType = type;
                _cachedReader = BuildReader(type);
            }

            return _cachedReader is null ? 0 : _cachedReader(request);
        }

        private static Func<object, int>? BuildReader(Type type)
        {
            // Try field with lowercase 'd' (VS convention for instance fields).
            var field = type.GetField("dimension", AnyInstance) ?? type.GetField("Dimension", AnyInstance);
            if (field is not null && field.FieldType == typeof(int))
            {
                return obj => (int)field.GetValue(obj)!;
            }

            // Fall back to property.
            var prop = type.GetProperty("dimension", AnyInstance) ?? type.GetProperty("Dimension", AnyInstance);
            if (prop is not null && prop.PropertyType == typeof(int) && prop.CanRead)
            {
                return obj => (int)prop.GetValue(obj)!;
            }

            return null;
        }
    }
}
