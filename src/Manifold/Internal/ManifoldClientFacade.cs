using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Client;
using Manifold.Api.Events;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>Client-side facade implementing the public <see cref="IManifoldClient"/> interface.</summary>
internal sealed class ManifoldClientFacade : IManifoldClient
{
    private readonly ClientDimensionMirror _mirror;

    /// <summary>Initializes a new instance of the <see cref="ManifoldClientFacade"/> class.</summary>
    /// <param name="mirror">Client-side dimension mirror; raises this facade's public events.</param>
    public ManifoldClientFacade(ClientDimensionMirror mirror)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _mirror.Added += dim => Created?.Invoke(this, new DimensionCreatedEventArgs(dim));
        _mirror.Removed += dim => Destroyed?.Invoke(this, new DimensionDestroyedEventArgs(dim));
    }

    /// <inheritdoc/>
    public event EventHandler<DimensionCreatedEventArgs>? Created;

    /// <inheritdoc/>
    public event EventHandler<DimensionDestroyedEventArgs>? Destroyed;

    /// <inheritdoc/>
    public event EventHandler<PlayerEnteredDimensionEventArgs>? LocalPlayerTransited;

    /// <summary>Gets a value indicating whether Manifold loaded healthily on the server. Settable internally by ModSystem.</summary>
    public bool IsHealthy { get; internal set; } = true;

    /// <inheritdoc/>
    public IReadOnlyCollection<IDimension> Dimensions => _mirror.All;

    /// <inheritdoc/>
    public IDimension? Get(AssetLocation code) => _mirror.Get(code);

    /// <summary>Internal: raise <see cref="LocalPlayerTransited"/>.</summary>
    /// <param name="args">Event args.</param>
    internal void RaiseLocalPlayerTransited(PlayerEnteredDimensionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        LocalPlayerTransited?.Invoke(this, args);
    }
}
