using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Api.Worldgen;
using Manifold.Internal.Util;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Fluent <see cref="IDimensionBuilder"/> implementation. Single-use; finalises via a completion delegate.
/// </summary>
/// <remarks>Server-side, main thread only. Not thread-safe.</remarks>
internal sealed class DimensionBuilderImpl : IDimensionBuilder
{
    /// <summary>Default generation radius in chunks (produces a 5x5 column region).</summary>
    internal const int DefaultGenerationRadius = 2;

    /// <summary>Default upper Y bound for the post-generation relight pass.</summary>
    internal const int DefaultRelightHeight = 20;

    /// <summary>Default per-dimension streaming column budget per tick.</summary>
    internal const int DefaultStreamingBudgetPerTick = 4;

    /// <summary>Empty metadata sentinel used when no <c>WithMetadata</c> was called. Immutable so it
    /// cannot be mutated through a downcast of the shared instance.</summary>
    internal static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        ImmutableDictionary<string, object?>.Empty;

    private readonly AssetLocation _code;
    private readonly string _ownerModId;
    private readonly System.Func<DimensionBuildRequest, IDimension> _completion;
    private IWorldgenStrategy? _worldgen;
    private DimensionLifetime? _lifetime;
    private int _generationRadius = DefaultGenerationRadius;
    private SpawnBehavior _spawnBehavior = SpawnBehavior.SameCoordinates;
    private BlockPos? _spawnPoint;
    private EnumGameMode? _forcedGameMode;
    private int? _streamingLoadRadius;
    private ManifoldInventory _separateInventory = ManifoldInventory.None;
    private int _relightHeight = DefaultRelightHeight;
    private int? _streamingBudgetPerTick;
    private int? _skyCapY;
    private Dictionary<string, object?>? _metadata;
    private bool _used;

    /// <summary>
    /// Initializes a new instance of the <see cref="DimensionBuilderImpl"/> class.
    /// </summary>
    /// <param name="code">The dimension code.</param>
    /// <param name="ownerModId">The mod registering the dimension.</param>
    /// <param name="completion">Callback invoked when RegisterStatic/Create is called.</param>
    internal DimensionBuilderImpl(
        AssetLocation code,
        string ownerModId,
        System.Func<DimensionBuildRequest, IDimension> completion)
    {
        _code = code ?? throw new ArgumentNullException(nameof(code));
        _ownerModId = Guards.NotNullOrWhiteSpace(ownerModId, nameof(ownerModId));
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithWorldgen(IWorldgenStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ThrowIfUsed();
        _worldgen = strategy;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder Persistent()
    {
        ThrowIfUsed();
        if (_lifetime == DimensionLifetime.Ephemeral)
        {
            throw new InvalidOperationException("Lifetime already set to Ephemeral.");
        }

        _lifetime = DimensionLifetime.Persistent;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder Ephemeral()
    {
        ThrowIfUsed();
        if (_lifetime == DimensionLifetime.Persistent)
        {
            throw new InvalidOperationException("Lifetime already set to Persistent.");
        }

        _lifetime = DimensionLifetime.Ephemeral;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithGenerationRadius(int chunks)
    {
        ThrowIfUsed();
        _generationRadius = Guards.InRange(chunks, 0, 16, nameof(chunks));
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithRelightHeight(int maxY)
    {
        ThrowIfUsed();
        _relightHeight = Guards.InRange(maxY, 1, 1024, nameof(maxY));
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithSpawnBehavior(SpawnBehavior behavior)
    {
        ThrowIfUsed();
        _spawnBehavior = behavior;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithFixedSpawn(BlockPos spawnPoint)
    {
        ArgumentNullException.ThrowIfNull(spawnPoint);
        ThrowIfUsed();
        _spawnPoint = spawnPoint;
        _spawnBehavior = SpawnBehavior.DimensionSpawn;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithForcedGameMode(EnumGameMode mode)
    {
        ThrowIfUsed();
        _forcedGameMode = mode;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder Streaming(int loadRadius)
    {
        ThrowIfUsed();
        _streamingLoadRadius = Guards.InRange(loadRadius, 1, 32, nameof(loadRadius));
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithStreamingBudget(int maxColumnsPerTick)
    {
        ThrowIfUsed();
        _streamingBudgetPerTick = Guards.InRange(maxColumnsPerTick, 1, 64, nameof(maxColumnsPerTick));
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithDarkSky(int ceilingY)
    {
        ThrowIfUsed();
        _skyCapY = Guards.InRange(ceilingY, 1, 1024, nameof(ceilingY));

        // The cap only takes effect if it sits inside the relit band: Manifold's bounded relight
        // clears + recomputes light only up to RelightHeight, so a cap above that height is never
        // recomputed and the dimension stays bright. Auto-raise the band to cover the cap layer.
        _relightHeight = System.Math.Max(_relightHeight, _skyCapY.Value + 1);
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithSeparateInventory(ManifoldInventory categories)
    {
        ThrowIfUsed();
        _separateInventory = categories;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithMetadata(string key, object? value)
    {
        ThrowIfUsed();
        Guards.NotNullOrWhiteSpace(key, nameof(key));
        if (value is not null && !IsSupportedMetadataType(value.GetType()))
        {
            throw new ArgumentException(
                $"Unsupported metadata value type '{value.GetType()}' for key '{key}'. " +
                "Supported types: primitives, string, enum, byte[].",
                nameof(value));
        }

        _metadata ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!_metadata.TryAdd(key, value))
        {
            throw new ArgumentException(
                $"Metadata key '{key}' is already set on this builder.",
                nameof(key));
        }

        return this;
    }

    /// <inheritdoc/>
    public IDimension RegisterStatic()
    {
        ThrowIfUsed();
        if (_worldgen is null)
        {
            throw new WorldgenStrategyContractException(
                $"Dimension '{_code}' must call WithWorldgen before RegisterStatic.");
        }

        var lifetime = _lifetime ?? DimensionLifetime.Persistent;
        if (lifetime == DimensionLifetime.Ephemeral)
        {
            throw new InvalidOperationException(
                "RegisterStatic cannot be combined with Ephemeral lifetime; use Create instead.");
        }

        _used = true;
        return _completion(new DimensionBuildRequest(
            _code,
            _worldgen,
            lifetime,
            _ownerModId,
            IsStaticRegistration: true,
            _generationRadius,
            _spawnBehavior,
            _spawnPoint,
            _forcedGameMode,
            _streamingLoadRadius,
            _relightHeight,
            _separateInventory,
            BuildMetadata(),
            _streamingBudgetPerTick,
            _skyCapY));
    }

    /// <inheritdoc/>
    public IDimension Create()
    {
        ThrowIfUsed();
        if (_worldgen is null)
        {
            throw new WorldgenStrategyContractException(
                $"Dimension '{_code}' must call WithWorldgen before Create.");
        }

        if (_lifetime is null)
        {
            throw new DimensionLifetimeUnspecifiedException(
                $"Dimension '{_code}' was Created without an explicit lifetime; call Persistent() or Ephemeral().");
        }

        _used = true;
        return _completion(new DimensionBuildRequest(
            _code,
            _worldgen,
            _lifetime.Value,
            _ownerModId,
            IsStaticRegistration: false,
            _generationRadius,
            _spawnBehavior,
            _spawnPoint,
            _forcedGameMode,
            _streamingLoadRadius,
            _relightHeight,
            _separateInventory,
            BuildMetadata(),
            _streamingBudgetPerTick,
            _skyCapY));
    }

    private static bool IsSupportedMetadataType(Type t) =>
        t.IsPrimitive || t == typeof(string) || t.IsEnum || t == typeof(byte[]);

    /// <summary>
    /// Snapshots the builder's metadata into an immutable dictionary stored on the dimension, so the
    /// published <c>IReadOnlyDictionary</c> cannot be mutated through a downcast back to Dictionary.
    /// </summary>
    private IReadOnlyDictionary<string, object?> BuildMetadata() =>
        _metadata is null ? EmptyMetadata : _metadata.ToImmutableDictionary(StringComparer.Ordinal);

    private void ThrowIfUsed()
    {
        if (_used)
        {
            throw new InvalidOperationException("DimensionBuilder is single-use; obtain a new one via Registry.Define.");
        }
    }
}
