using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Api.Worldgen;
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

    /// <summary>Default per-dimension streaming column budget per tick.</summary>
    internal const int DefaultStreamingBudgetPerTick = 4;

    /// <summary>Empty metadata sentinel used when no <c>WithMetadata</c> was called. Immutable so it
    /// cannot be mutated through a downcast of the shared instance.</summary>
    internal static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        ImmutableDictionary<string, object?>.Empty;

    private readonly AssetLocation _code;
    private readonly string _ownerModId;
    private readonly System.Func<DimensionImpl, IDimension> _completion;
    private IWorldgenStrategy? _worldgen;
    private DimensionLifetime? _lifetime;
    private int _generationRadius = DefaultGenerationRadius;
    private SpawnBehavior _spawnBehavior = SpawnBehavior.SameCoordinates;
    private BlockPos? _spawnPoint;
    private EnumGameMode? _forcedGameMode;
    private int? _streamingLoadRadius;
    private ManifoldInventory _separateInventory = ManifoldInventory.None;
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
        System.Func<DimensionImpl, IDimension> completion)
    {
        _code = code ?? throw new ArgumentNullException(nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModId);
        _ownerModId = ownerModId;
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
        ArgumentOutOfRangeException.ThrowIfLessThan(chunks, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunks, 16);
        _generationRadius = chunks;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithRelightHeight(int maxY)
    {
        ThrowIfUsed();
        ArgumentOutOfRangeException.ThrowIfLessThan(maxY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxY, 1024);
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
        ArgumentOutOfRangeException.ThrowIfLessThan(loadRadius, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(loadRadius, 32);
        _streamingLoadRadius = loadRadius;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithStreamingBudget(int maxColumnsPerTick)
    {
        ThrowIfUsed();
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColumnsPerTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColumnsPerTick, 64);
        _streamingBudgetPerTick = maxColumnsPerTick;
        return this;
    }

    /// <inheritdoc/>
    public IDimensionBuilder WithDarkSky(int ceilingY)
    {
        ThrowIfUsed();
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ceilingY, 1024);
        _skyCapY = ceilingY;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
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
        return _completion(BuildTemplate(lifetime));
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
        return _completion(BuildTemplate(_lifetime.Value));
    }

    /// <summary>
    /// Builds the dimension record for the registry to complete: <see cref="DimensionImpl.InternalId"/>
    /// is a placeholder the registry overwrites (a fresh id, or the existing id when promoting a
    /// Pending entry).
    /// </summary>
    private DimensionImpl BuildTemplate(DimensionLifetime lifetime) => new(
        Code: _code,
        InternalId: 0,
        IsBuiltIn: false,
        Lifetime: lifetime,
        OwnerModId: _ownerModId,
        State: DimensionState.Active,
        Worldgen: _worldgen,
        GenerationRadius: _generationRadius,
        SpawnBehavior: _spawnBehavior,
        SpawnPoint: _spawnPoint,
        ForcedGameMode: _forcedGameMode,
        StreamingLoadRadius: _streamingLoadRadius,
        SeparateInventory: _separateInventory,
        Metadata: BuildMetadata(),
        StreamingBudgetPerTick: _streamingBudgetPerTick,
        SkyCapY: _skyCapY);

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
