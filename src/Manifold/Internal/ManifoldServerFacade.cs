using System;
using Manifold.Api.Server;

namespace Manifold.Internal;

/// <summary>Server-side facade implementing the public <see cref="IManifoldServer"/> interface.</summary>
internal sealed class ManifoldServerFacade : IManifoldServer
{
    /// <summary>Initializes a new instance of the <see cref="ManifoldServerFacade"/> class.</summary>
    /// <param name="registry">Dimension registry.</param>
    /// <param name="transitions">Transit service.</param>
    /// <param name="isHealthy">Whether Harmony patches applied successfully.</param>
    public ManifoldServerFacade(
        IDimensionRegistry registry,
        ITransitionService transitions,
        bool isHealthy)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        IsHealthy = isHealthy;
    }

    /// <inheritdoc/>
    public IDimensionRegistry Registry { get; }

    /// <inheritdoc/>
    public ITransitionService Transitions { get; }

    /// <inheritdoc/>
    public bool IsHealthy { get; }
}
