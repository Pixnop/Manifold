using System;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
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

    /// <inheritdoc/>
    public bool ForceRemoveDimension(AssetLocation dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        if (!IsHealthy)
        {
            throw new ManifoldUnhealthyException(
                "Manifold patches failed at boot; dimension removal is disabled.");
        }

        var dim = Registry.Get(dimension);
        if (dim is null)
        {
            return false;
        }

        // Only ephemeral dimensions are removable. For BuiltIn/Persistent, let TryRemove throw the
        // canonical exception WITHOUT evacuating anyone first (never kick players then fail to remove).
        if (dim.Lifetime != DimensionLifetime.Ephemeral)
        {
            return Registry.TryRemove(dimension);
        }

        EvacuateOccupants(dim.InternalId);

        // Evacuating the last occupant fires PlayerLeft, whose transit-out auto-reap may have already
        // removed the now-empty dimension. If so, that is the success we wanted - report it as such
        // rather than letting a second TryRemove return false for a code that is already gone.
        return Registry.Get(dimension) is null || Registry.TryRemove(dimension);
    }

    /// <summary>
    /// Teleports every connected player currently inside <paramref name="internalId"/> back to the
    /// overworld (last-visited position) so the dimension can then be removed. Best-effort per player.
    /// </summary>
    private void EvacuateOccupants(int internalId)
    {
        foreach (var p in _sapi.World.AllOnlinePlayers)
        {
            if (p is IServerPlayer sp && EntityPosAccess.PosOrNull(sp.Entity)?.Dimension == internalId)
            {
                try
                {
                    Transitions.TeleportPlayer(
                        sp,
                        new AssetLocation("manifold", "overworld"),
                        new TransitionOptions { SpawnBehavior = SpawnBehavior.LastVisited });
                }
                catch
                {
                    // Best-effort: a teleport failure leaves the player in place and TryRemove will
                    // then refuse, so we never remove a dimension that still has someone inside.
                }
            }
        }
    }
}
