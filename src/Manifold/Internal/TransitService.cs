using System;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Server-side implementation of <see cref="ITransitionService"/>.</summary>
/// <remarks>Main thread only.</remarks>
internal sealed class TransitService : ITransitionService
{
    private const string InventoryModdataKey = "manifold:inv";

    private readonly DimensionRegistry _registry;
    private readonly ICoreServerAPI _sapi;
    private readonly TransitMovers _movers;
    private readonly ITargetPositionResolver _defaultResolver;
    private readonly DimensionGenerator _generator;
    private readonly PlayerPositionStore _positionStore;
    private readonly InventorySwapper _inventory;
    private bool _unhealthy;

    /// <summary>Initializes a new instance of the <see cref="TransitService"/> class.</summary>
    /// <param name="registry">Dimension registry.</param>
    /// <param name="sapi">Server API.</param>
    /// <param name="movers">Player teleporter and entity re-home primitives (grouped to keep arity reasonable).</param>
    /// <param name="defaultResolver">Default target position resolver (used for SameCoordinates behavior).</param>
    /// <param name="generator">Dimension generator for pre-generating destination chunks.</param>
    /// <param name="positionStore">Per-player per-dimension last-position memory (for LastVisited behavior).</param>
    /// <param name="inventory">Inventory swapper for per-dimension inventory separation.</param>
    internal TransitService(
        DimensionRegistry registry,
        ICoreServerAPI sapi,
        TransitMovers movers,
        ITargetPositionResolver defaultResolver,
        DimensionGenerator generator,
        PlayerPositionStore positionStore,
        InventorySwapper inventory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
        _movers = (movers ?? throw new ArgumentNullException(nameof(movers))).Required();
        _defaultResolver = defaultResolver ?? throw new ArgumentNullException(nameof(defaultResolver));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _positionStore = positionStore ?? throw new ArgumentNullException(nameof(positionStore));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    /// <inheritdoc/>
    public event EventHandler<PlayerEnteringDimensionEventArgs>? PlayerEntering;

    /// <inheritdoc/>
    public event EventHandler<PlayerArrivingDimensionEventArgs>? PlayerArriving;

    /// <inheritdoc/>
    public event EventHandler<PlayerEnteredDimensionEventArgs>? PlayerEntered;

    /// <inheritdoc/>
    public event EventHandler<PlayerLeftDimensionEventArgs>? PlayerLeft;

    /// <inheritdoc/>
    public event EventHandler<EntityChangedDimensionEventArgs>? EntityChangedDimension;

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

        int sourceId = EntityPosAccess.Pos(player.Entity).Dimension;
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
        var srcPos = EntityPosAccess.Pos(player.Entity);
        _positionStore.Record(player.PlayerUID, sourceId, (int)srcPos.X, (int)srcPos.Y, (int)srcPos.Z);

        // Pre-generate / load the destination region so the player lands on solid ground.
        _generator.EnsureRegion(_sapi, target.InternalId, prelim.X / 32, prelim.Z / 32, player);

        // Resolve the final landing position now that terrain exists.
        var targetPos = ResolveTargetPosition(player, target, targetImpl, options);

        // Post-generation, pre-teleport hook: subscribers can finalize landing setup or veto.
        var arrivingArgs = new PlayerArrivingDimensionEventArgs(player, source, target, targetPos);
        PlayerArriving?.Invoke(this, arrivingArgs);
        if (arrivingArgs.Cancel)
        {
            return;
        }

        _movers.Player.Teleport(player, targetPos);

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
                // Best effort - never block transit on a game-mode failure.
            }
        }

        ApplyInventoryPolicy(player, target, targetImpl);

        PlayerLeft?.Invoke(this, new PlayerLeftDimensionEventArgs(player, source, target));
        PlayerEntered?.Invoke(this, new PlayerEnteredDimensionEventArgs(player, source, target));
    }

    /// <inheritdoc/>
    public bool TeleportBlock(BlockPos source, AssetLocation targetDim, BlockPos targetLocal)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetDim);
        ArgumentNullException.ThrowIfNull(targetLocal);
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

        var targetPos = targetLocal.Copy();
        targetPos.dimension = target.InternalId;

        // Pre-generate the destination region so the target column is loaded before we write the
        // block. Same pre-load contract as TeleportEntity.
        _generator.EnsureRegion(_sapi, target.InternalId, targetPos.X / 32, targetPos.Z / 32, null);

        return _movers.Block.Move(source, targetPos);
    }

    /// <inheritdoc/>
    public void TeleportEntity(Entity entity, AssetLocation targetDim, TransitionOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(targetDim);
        if (_unhealthy)
        {
            throw new ManifoldUnhealthyException(
                "Manifold patches failed at boot; transit is disabled.");
        }

        if (entity is EntityPlayer)
        {
            throw new ArgumentException(
                "TeleportEntity is for non-player entities; use TeleportPlayer for players.",
                nameof(entity));
        }

        var target = _registry.Get(targetDim)
            ?? throw new DimensionNotFoundException($"No dimension registered with code '{targetDim}'.");
        if (target.State != DimensionState.Active)
        {
            throw new DimensionStateException(
                $"Dimension '{targetDim}' is in state {target.State}; transit not allowed.");
        }

        // Capture source dim BEFORE the move so the post-event reports the actual previous
        // dimension. The default-to-overworld fallback mirrors TeleportPlayer's handling of
        // entities whose pos.dimension does not (yet) match a registered dim.
        int sourceId = EntityPosAccess.Pos(entity).Dimension;
        var source = _registry.GetByInternalId(sourceId) ?? _registry.GetByInternalId(0)!;

        // Preliminary position to center generation, then a final position after terrain exists.
        var prelim = ResolveEntityPosition(entity, target, options);
        _generator.EnsureRegion(_sapi, target.InternalId, prelim.X / 32, prelim.Z / 32, null);
        var finalPos = ResolveEntityPosition(entity, target, options);

        _movers.Entity.Move(entity, finalPos);

        EntityChangedDimension?.Invoke(this, new EntityChangedDimensionEventArgs(entity, source, target, finalPos));
    }

    /// <summary>Mark the service as unhealthy (called when Harmony patches fail at boot).</summary>
    internal void MarkUnhealthy() => _unhealthy = true;

    /// <summary>
    /// Swaps the player's separated inventory categories to the destination dimension's profile.
    /// Snapshots the current contents before clearing, restores the destination set (empty on first
    /// visit), and persists the profiles in the player's moddata so they save with the inventory.
    /// </summary>
    private void ApplyInventoryPolicy(IServerPlayer player, IDimension target, DimensionImpl? targetImpl)
    {
        var store = PlayerInventoryStore.FromBytes(player.GetModdata(InventoryModdataKey));
        var plan = InventoryProfileResolver.Plan(
            targetImpl?.SeparateInventory ?? ManifoldInventory.None,
            target.Code.ToString(),
            store.CurrentKey);

        if (plan.Count == 0)
        {
            return;
        }

        foreach (var swap in plan)
        {
            // Snapshot the current contents into the source key BEFORE touching any slot.
            store.SetSnapshot(swap.Category, swap.FromKey, InventorySwapper.Serialize(player, swap.Category));

            if (store.HasSnapshot(swap.Category, swap.ToKey))
            {
                _inventory.Restore(player, swap.Category, store.GetSnapshot(swap.Category, swap.ToKey)!);
            }
            else
            {
                InventorySwapper.Clear(player, swap.Category);
            }

            store.SetCurrentKey(swap.Category, swap.ToKey);
        }

        player.SetModdata(InventoryModdataKey, store.ToBytes());
    }

    /// <summary>
    /// Landing position for a non-player entity: per-transit override, else the resolver (default
    /// <see cref="TargetPositionResolvers.SameXZSurfaceY"/>). No spawn-behavior / last-visited (player-only).
    /// </summary>
    private BlockPos ResolveEntityPosition(Entity entity, IDimension target, TransitionOptions options)
    {
        if (options.OverridePosition is { } overridePos)
        {
            return overridePos.SetDimension(target.InternalId);
        }

        var resolver = options.Resolver ?? _defaultResolver;
        return resolver.Resolve(entity, target, _sapi).SetDimension(target.InternalId);
    }

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
            return resolver.Resolve(player.Entity, target, _sapi).SetDimension(target.InternalId);
        }

        // Per-transit override beats the dimension's configured behavior.
        var behavior = options.SpawnBehavior ?? targetImpl?.SpawnBehavior ?? SpawnBehavior.SameCoordinates;
        switch (behavior)
        {
            case SpawnBehavior.DimensionSpawn:
                var spawn = targetImpl?.SpawnPoint ?? new BlockPos(0, 64, 0, target.InternalId);
                return TargetPositionResolvers.FixedSpawn(spawn)
                    .Resolve(player.Entity, target, _sapi)
                    .SetDimension(target.InternalId);

            case SpawnBehavior.LastVisited:
                if (_positionStore.TryGet(player.PlayerUID, target.InternalId, out var x, out var y, out var z))
                {
                    return new BlockPos(x, y, z, target.InternalId);
                }

                return _defaultResolver.Resolve(player.Entity, target, _sapi).SetDimension(target.InternalId);

            default:
                return _defaultResolver.Resolve(player.Entity, target, _sapi).SetDimension(target.InternalId);
        }
    }
}
