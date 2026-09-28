using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Client;
using Manifold.Api.Events;
using Manifold.Internal.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Manifold.Internal;

/// <summary>Client-side facade implementing the public <see cref="IManifoldClient"/> interface.</summary>
internal sealed class ManifoldClientFacade : IManifoldClient
{
    private readonly ClientDimensionMirror _mirror;
    private readonly ILogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="ManifoldClientFacade"/> class.</summary>
    /// <param name="mirror">Client-side dimension mirror; raises this facade's public events.</param>
    /// <param name="transitHandler">
    /// Resolves incoming transit packets and raises <see cref="LocalPlayerChangedDimension"/>; <c>null</c>
    /// if the caller has none (the facade then never raises that event, e.g. in a test double).
    /// </param>
    /// <param name="logger">
    /// Optional logger used to report (and swallow) exceptions thrown by third-party
    /// <see cref="Created"/>/<see cref="Destroyed"/>/<see cref="LocalPlayerChangedDimension"/> subscribers.
    /// <c>null</c> silences the report.
    /// </param>
    public ManifoldClientFacade(ClientDimensionMirror mirror, ClientTransitHandler? transitHandler = null, ILogger? logger = null)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _logger = logger;

        // SafeEvent isolates a throwing third-party subscriber, same as the server-side facade: one
        // faulting mod must not abort delivery to the others or propagate into the network handler.
        _mirror.Added += dim =>
            SafeEvent.Raise(Created, this, new DimensionCreatedEventArgs(dim), LogSubscriberError);
        _mirror.Removed += dim =>
            SafeEvent.Raise(Destroyed, this, new DimensionDestroyedEventArgs(dim), LogSubscriberError);

        if (transitHandler is not null)
        {
            transitHandler.Transited += args =>
                SafeEvent.Raise(LocalPlayerChangedDimension, this, args, LogSubscriberError);
        }
    }

    /// <inheritdoc/>
    public event EventHandler<DimensionCreatedEventArgs>? Created;

    /// <inheritdoc/>
    public event EventHandler<DimensionDestroyedEventArgs>? Destroyed;

    /// <inheritdoc/>
    // Never raised - kept for binary compatibility (see IManifoldClient.LocalPlayerTransited).
#pragma warning disable CS0067
    public event EventHandler<PlayerEnteredDimensionEventArgs>? LocalPlayerTransited;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public event EventHandler<LocalPlayerDimensionChangedEventArgs>? LocalPlayerChangedDimension;

    /// <inheritdoc/>
    public bool IsHealthy => true;

    /// <inheritdoc/>
    public IReadOnlyCollection<IDimension> Dimensions => _mirror.All;

    /// <inheritdoc/>
    public IDimension? Get(AssetLocation code) => _mirror.Get(code);

    /// <inheritdoc/>
    public IDimension? GetDimensionOf(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _mirror.GetByInternalId(EntityPosAccess.Pos(entity).Dimension);
    }

    private void LogSubscriberError(Exception ex) =>
        _logger?.Warning("[Manifold] A client dimension event subscriber threw and was isolated: {0}", ex);
}
