using System;
using Manifold.Api;
using Manifold.Api.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Server-side facade implementing the public <see cref="IManifoldServer"/> interface.</summary>
internal sealed class ManifoldServerFacade : IManifoldServer
{
    private readonly ICoreServerAPI _sapi;

    /// <summary>Initializes a new instance of the <see cref="ManifoldServerFacade"/> class.</summary>
    /// <param name="registry">Dimension registry.</param>
    /// <param name="transitions">Transit service.</param>
    /// <param name="sapi">Server API (used by <see cref="RelightRegion"/>).</param>
    /// <param name="isHealthy">Whether Harmony patches applied successfully.</param>
    public ManifoldServerFacade(
        IDimensionRegistry registry,
        ITransitionService transitions,
        ICoreServerAPI sapi,
        bool isHealthy)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
        IsHealthy = isHealthy;
    }

    /// <inheritdoc/>
    public IDimensionRegistry Registry { get; }

    /// <inheritdoc/>
    public ITransitionService Transitions { get; }

    /// <inheritdoc/>
    public bool IsHealthy { get; }

    /// <inheritdoc/>
    public void RelightRegion(AssetLocation dimension, BlockPos min, BlockPos max)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        if (!IsHealthy)
        {
            throw new ManifoldUnhealthyException(
                "Manifold patches failed at boot; relight is disabled.");
        }

        var dim = Registry.Get(dimension)
            ?? throw new DimensionNotFoundException($"No dimension registered with code '{dimension}'.");

        // Runtime relight (consumer placed blocks at runtime): push to clients so the change is
        // visible - the chunks are already loaded client-side, the server light alone is invisible.
        DimensionGenerator.RelightBlockBounds(_sapi, dim.InternalId, min, max, sendToClients: true);
    }
}
