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
    private readonly PlayerPositionStore _positionStore;
    private bool _unhealthy;

    /// <summary>Initializes a new instance of the <see cref="TransitService"/> class.</summary>
    /// <param name="registry">Dimension registry.</param>
    /// <param name="sapi">Server API.</param>
    /// <param name="teleporter">Player teleporter abstraction.</param>
    /// <param name="defaultResolver">Default target position resolver (used for SameCoordinates behavior).</param>
    /// <param name="generator">Dimension generator for pre-generating destination chunks.</param>
    /// <param name="positionStore">Per-player per-dimension last-position memory (for LastVisited behavior).</param>
    internal TransitService(
        DimensionRegistry registry,
        ICoreServerAPI sapi,
        IPlayerTeleporter teleporter,
        ITargetPositionResolver defaultResolver,
        DimensionGenerator generator,
        PlayerPositionStore positionStore)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
        _teleporter = teleporter ?? throw new ArgumentNullException(nameof(teleporter));
        _defaultResolver = defaultResolver ?? throw new ArgumentNullException(nameof(defaultResolver));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _positionStore = positionStore ?? throw new ArgumentNullException(nameof(positionStore));
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
        var targetImpl = _registry.GetByInternalId(target.InternalId);

        // Resolve a preliminary position to determine the generation region center.
        // The resolver may be called again after generation (see below), so implementations
        // must be deterministic and side-effect free.
        var prelim = ResolveTargetPosition(player, target, targetImpl, options);

        var enteringArgs = new PlayerEnteringDimensionEventArgs(player, source, target, prelim);
        PlayerEntering?.Invoke(this, enteringArgs);
        if (enteringArgs.Cancel)
        {
            return;
        }

        // Record the player's current position in the SOURCE dimension before leaving,
        // so the LastVisited behavior can return them here later.
        var srcPos = player.Entity.Pos;
        _positionStore.Record(player.PlayerUID, sourceId, (int)srcPos.X, (int)srcPos.Y, (int)srcPos.Z);

        // Pre-generate / load the destination region so the player lands on solid ground.
        _generator.EnsureRegion(_sapi, target.InternalId, prelim.X / 32, prelim.Z / 32, player);

        // Resolve the final landing position now that terrain exists.
        var targetPos = ResolveTargetPosition(player, target, targetImpl, options);

        _teleporter.Teleport(player, targetPos);

        // Apply a forced game mode if the destination dimension configures one.
        if (targetImpl?.ForcedGameMode is { } gameMode)
        {
            try
            {
                player.WorldData.CurrentGameMode = gameMode;
                player.BroadcastPlayerData(true);
            }
            catch
            {
                // Best effort — never block transit on a game-mode failure.
            }
        }

        PlayerLeft?.Invoke(this, new PlayerLeftDimensionEventArgs(player, source, target));
        PlayerEntered?.Invoke(this, new PlayerEnteredDimensionEventArgs(player, source, target));
    }

    /// <summary>Mark the service as unhealthy (called when Harmony patches fail at boot).</summary>
    internal void MarkUnhealthy() => _unhealthy = true;

    /// <summary>
    /// Computes the landing position, honoring per-transit overrides first, then the target
    /// dimension's <see cref="SpawnBehavior"/>.
    /// </summary>
    private BlockPos ResolveTargetPosition(
        IServerPlayer player, IDimension target, DimensionImpl? targetImpl, TransitionOptions options)
    {
        if (options.OverridePosition is { } overridePos)
        {
            return overridePos.SetDimension(target.InternalId);
        }

        if (options.Resolver is { } resolver)
        {
            return resolver.Resolve(player, target, _sapi).SetDimension(target.InternalId);
        }

        // Per-transit override beats the dimension's configured behavior.
        var behavior = options.SpawnBehavior ?? targetImpl?.SpawnBehavior ?? SpawnBehavior.SameCoordinates;
        switch (behavior)
        {
            case SpawnBehavior.DimensionSpawn:
                var spawn = targetImpl?.SpawnPoint ?? new BlockPos(0, 64, 0, target.InternalId);
                return TargetPositionResolvers.FixedSpawn(spawn)
                    .Resolve(player, target, _sapi)
                    .SetDimension(target.InternalId);

            case SpawnBehavior.LastVisited:
                if (_positionStore.TryGet(player.PlayerUID, target.InternalId, out var x, out var y, out var z))
                {
                    return new BlockPos(x, y, z, target.InternalId);
                }

                return _defaultResolver.Resolve(player, target, _sapi).SetDimension(target.InternalId);

            default:
                return _defaultResolver.Resolve(player, target, _sapi).SetDimension(target.InternalId);
        }
    }
}
