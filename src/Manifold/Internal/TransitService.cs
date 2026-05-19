using System;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Server-side implementation of <see cref="ITransitionService"/>.</summary>
/// <remarks>Main thread only.</remarks>
internal sealed class TransitService : ITransitionService
{
    private readonly DimensionRegistry _registry;
    private readonly ICoreServerAPI _sapi;
    private readonly IPlayerTeleporter _teleporter;
    private readonly ITargetPositionResolver _defaultResolver;
    private readonly DimensionGenerator _generator;
    private bool _unhealthy;

    /// <summary>Initializes a new instance of the <see cref="TransitService"/> class.</summary>
    /// <param name="registry">Dimension registry.</param>
    /// <param name="sapi">Server API.</param>
    /// <param name="teleporter">Player teleporter abstraction.</param>
    /// <param name="defaultResolver">Default target position resolver.</param>
    /// <param name="generator">Dimension generator for pre-generating destination chunks.</param>
    internal TransitService(
        DimensionRegistry registry,
        ICoreServerAPI sapi,
        IPlayerTeleporter teleporter,
        ITargetPositionResolver defaultResolver,
        DimensionGenerator generator)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
        _teleporter = teleporter ?? throw new ArgumentNullException(nameof(teleporter));
        _defaultResolver = defaultResolver ?? throw new ArgumentNullException(nameof(defaultResolver));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    /// <inheritdoc/>
    public event EventHandler<PlayerEnteringDimensionEventArgs>? PlayerEntering;

    /// <inheritdoc/>
    public event EventHandler<PlayerEnteredDimensionEventArgs>? PlayerEntered;

    /// <inheritdoc/>
    public event EventHandler<PlayerLeftDimensionEventArgs>? PlayerLeft;

    /// <inheritdoc/>
    public void TeleportPlayer(IServerPlayer player, AssetLocation targetDim, TransitionOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(targetDim);
        if (_unhealthy)
        {
            throw new ManifoldUnhealthyException(
                "Manifold patches failed at boot; transit is disabled.");
        }

        var target = _registry.Get(targetDim)
            ?? throw new DimensionNotFoundException($"No dimension registered with code '{targetDim}'.");
        if (target.State != DimensionState.Active)
        {
            throw new DimensionStateException(
                $"Dimension '{targetDim}' is in state {target.State}; transit not allowed.");
        }

        int sourceId = player.Entity.Pos.Dimension;
        var source = _registry.GetByInternalId(sourceId) ?? _registry.GetByInternalId(0)!;

        BlockPos targetPos = options.OverridePosition
            ?? (options.Resolver ?? _defaultResolver).Resolve(player, target, _sapi);
        targetPos = targetPos.SetDimension(target.InternalId);

        var enteringArgs = new PlayerEnteringDimensionEventArgs(player, source, target, targetPos);
        PlayerEntering?.Invoke(this, enteringArgs);
        if (enteringArgs.Cancel)
        {
            return;
        }

        // Pre-generate / load the destination region so the player lands on solid ground.
        int centerCx = targetPos.X / 32;
        int centerCz = targetPos.Z / 32;
        _generator.EnsureRegion(_sapi, target.InternalId, centerCx, centerCz, player);

        _teleporter.Teleport(player, targetPos);

        PlayerLeft?.Invoke(this, new PlayerLeftDimensionEventArgs(player, source, target));
        PlayerEntered?.Invoke(this, new PlayerEnteredDimensionEventArgs(player, source, target));
    }

    /// <summary>Mark the service as unhealthy (called when Harmony patches fail at boot).</summary>
    internal void MarkUnhealthy() => _unhealthy = true;
}
