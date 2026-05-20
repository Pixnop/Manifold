using System;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Worldgen;
using Manifold.Internal.Util;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Fluent <see cref="IDimensionBuilder"/> implementation. Single-use; finalises via a completion delegate.
/// </summary>
/// <remarks>Server-side, main thread only. Not thread-safe.</remarks>
internal sealed class DimensionBuilderImpl : IDimensionBuilder
{
    /// <summary>Default generation radius in chunks (produces a 5x5 column region).</summary>
    internal const int DefaultGenerationRadius = 2;

    private readonly AssetLocation _code;
    private readonly string _ownerModId;
    private readonly System.Func<DimensionBuildRequest, IDimension> _completion;
    private IWorldgenStrategy? _worldgen;
    private DimensionLifetime? _lifetime;
    private int _generationRadius = DefaultGenerationRadius;
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
        return _completion(new DimensionBuildRequest(_code, _worldgen, lifetime, _ownerModId, IsStaticRegistration: true, _generationRadius));
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
        return _completion(new DimensionBuildRequest(_code, _worldgen, _lifetime.Value, _ownerModId, IsStaticRegistration: false, _generationRadius));
    }

    private void ThrowIfUsed()
    {
        if (_used)
        {
            throw new InvalidOperationException("DimensionBuilder is single-use; obtain a new one via Registry.Define.");
        }
    }
}
