using System;
using Manifold.Api.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// An <see cref="IManifoldServer"/> bound to a consumer mod id. Its <see cref="Registry"/>
/// records dimensions under that owner. Transit, relight and health delegate to the shared facade.
/// </summary>
internal sealed class OwnerScopedManifoldServer : IManifoldServer
{
    private readonly IManifoldServer _shared;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnerScopedManifoldServer"/> class.
    /// </summary>
    /// <param name="shared">The shared Manifold server facade.</param>
    /// <param name="sharedRegistry">The shared dimension registry (typed for scoped access).</param>
    /// <param name="ownerModId">The mod id that owns dimensions registered through this server.</param>
    public OwnerScopedManifoldServer(IManifoldServer shared, DimensionRegistry sharedRegistry, string ownerModId)
    {
        ArgumentNullException.ThrowIfNull(shared);
        _shared = shared;
        Registry = new OwnerScopedRegistry(sharedRegistry, ownerModId);
        Transitions = shared.Transitions;
        IsHealthy = shared.IsHealthy;
    }

    /// <inheritdoc/>
    public IDimensionRegistry Registry { get; }

    /// <inheritdoc/>
    public ITransitionService Transitions { get; }

    /// <inheritdoc/>
    public bool IsHealthy { get; }

    /// <inheritdoc/>
    public void RelightRegion(AssetLocation dimension, BlockPos min, BlockPos max) =>
        _shared.RelightRegion(dimension, min, max);
}
