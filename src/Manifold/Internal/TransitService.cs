using System;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Internal.Util;
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
    private const string InventoryCorruptModdataKey = "manifold:inv.corrupt";
    private const string GameModeModdataKey = "manifold:gamemode-before-forced";

    private readonly DimensionRegistry _registry;
    private readonly ICoreServerAPI _sapi;
    private readonly TransitMovers _movers;
    private readonly ITargetPositionResolver _defaultResolver;
    private readonly DimensionGenerator _generator;
    private readonly PlayerPositionStore _positionStore;
    private readonly IInventorySwapper _inventory;
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
        IInventorySwapper inventory)
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
    public void TeleportPlayer(IServerPlayer player, AssetLocation targetDim, TransitionOptions options = default) =>
        TryTeleportPlayer(player, targetDim, options);

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
        _generator.EnsureRegion(_sapi, target.InternalId, ChunkMath.ToChunk(targetPos.X), ChunkMath.ToChunk(targetPos.Z), null);

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
        _generator.EnsureRegion(_sapi, target.InternalId, ChunkMath.ToChunk(prelim.X), ChunkMath.ToChunk(prelim.Z), null);
        var finalPos = ResolveEntityPosition(entity, target, options);

        _movers.Entity.Move(entity, finalPos);

        SafeEvent.Raise(
            EntityChangedDimension,
            this,
            new EntityChangedDimensionEventArgs(entity, source, target, finalPos),
            LogSubscriberError);
    }

    /// <summary>
    /// Core of <see cref="TeleportPlayer"/>: identical behavior, but reports whether the player
    /// actually moved (as opposed to a subscriber cancelling the transit). <see cref="TeleportPlayer"/>
    /// is <c>void</c> per <see cref="ITransitionService"/> and cannot report this without a breaking
    /// API change; <see cref="Api.Helpers.DimensionCommandBuilder"/> and <see cref="Api.Helpers.PortalBlockBase"/>
    /// call this directly (when <c>Transitions</c> is this concrete type) so they can reply with an
    /// error instead of reporting success on a cancelled transit.
    /// </summary>
    /// <param name="player">Server player to teleport.</param>
    /// <param name="targetDim">Target dimension code.</param>
    /// <param name="options">Optional transit settings.</param>
    /// <returns><c>true</c> if the player was moved; <c>false</c> if a subscriber cancelled the transit.</returns>
    internal bool TryTeleportPlayer(IServerPlayer player, AssetLocation targetDim, TransitionOptions options = default)
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
        SafeEvent.Raise(PlayerEntering, this, enteringArgs, LogSubscriberError);
        if (enteringArgs.Cancel)
        {
            _sapi.Logger?.Notification(
                "[Manifold] Transit of {0} to {1} cancelled at PlayerEntering.",
                player.PlayerName,
                target.Code);
            return false;
        }

        // Record the player's current position in the SOURCE dimension before leaving,
        // so the LastVisited behavior can return them here later.
        var srcPos = EntityPosAccess.Pos(player.Entity);
        _positionStore.Record(player.PlayerUID, sourceId, (int)srcPos.X, (int)srcPos.Y, (int)srcPos.Z);

        // Pre-generate / load the destination region so the player lands on solid ground.
        _generator.EnsureRegion(_sapi, target.InternalId, ChunkMath.ToChunk(prelim.X), ChunkMath.ToChunk(prelim.Z), player);

        // Resolve the final landing position now that terrain exists.
        var targetPos = ResolveTargetPosition(player, target, targetImpl, options);

        // Post-generation, pre-teleport hook: subscribers can finalize landing setup or veto.
        var arrivingArgs = new PlayerArrivingDimensionEventArgs(player, source, target, targetPos);
        SafeEvent.Raise(PlayerArriving, this, arrivingArgs, LogSubscriberError);
        if (arrivingArgs.Cancel)
        {
            _sapi.Logger?.Notification(
                "[Manifold] Transit of {0} to {1} cancelled at PlayerArriving: {2}",
                player.PlayerName,
                target.Code,
                arrivingArgs.CancellationReason ?? "(no reason)");
            return false;
        }

        _movers.Player.Teleport(player, targetPos);

        ApplyGameModePolicy(player, targetImpl);
        ApplyInventoryPolicy(player, target, targetImpl);

        SafeEvent.Raise(PlayerLeft, this, new PlayerLeftDimensionEventArgs(player, source, target), LogSubscriberError);
        SafeEvent.Raise(PlayerEntered, this, new PlayerEnteredDimensionEventArgs(player, source, target, targetPos), LogSubscriberError);
        return true;
    }

    /// <summary>Mark the service as unhealthy (called when Harmony patches fail at boot).</summary>
    internal void MarkUnhealthy() => _unhealthy = true;

    /// <summary>
    /// A forced game mode belongs to its dimension. The mode the player had before entering the
    /// first forced dimension is kept in their moddata (so it survives logout and restarts, like the
    /// inventory store) and handed back when they transit to a dimension that forces nothing.
    /// Chaining forced dimensions keeps the original mode, not the previous forced one.
    /// </summary>
    private void ApplyGameModePolicy(IServerPlayer player, DimensionImpl? targetImpl)
    {
        byte[]? saved = player.GetModdata(GameModeModdataKey);
        EnumGameMode mode;
        if (targetImpl?.ForcedGameMode is { } forced)
        {
            if (saved is null)
            {
                player.SetModdata(GameModeModdataKey, BitConverter.GetBytes((int)player.WorldData.CurrentGameMode));
            }

            mode = forced;
        }
        else if (saved is { Length: sizeof(int) })
        {
            player.RemoveModdata(GameModeModdataKey);
            mode = (EnumGameMode)BitConverter.ToInt32(saved, 0);
        }
        else
        {
            return;
        }

        try
        {
            player.WorldData.CurrentGameMode = mode;
            player.BroadcastPlayerData(true);
        }
        catch (Exception ex)
        {
            // Never block a transit that already happened on a game-mode failure.
            _sapi.Logger?.Warning("[Manifold] Could not set game mode {0} for {1}: {2}", mode, player.PlayerName, ex);
        }
    }

    private void LogSubscriberError(Exception ex) =>
        _sapi.Logger?.Warning("[Manifold] A transit event subscriber threw and was isolated: {0}", ex);

    /// <summary>
    /// Swaps the player's separated inventory categories to the destination dimension's profile.
    /// Snapshots the current contents before clearing, restores the destination set (empty on first
    /// visit), and persists the profiles in the player's moddata so they save with the inventory.
    /// </summary>
    private void ApplyInventoryPolicy(IServerPlayer player, IDimension target, DimensionImpl? targetImpl)
    {
        var raw = player.GetModdata(InventoryModdataKey);
        var store = PlayerInventoryStore.TryFromBytes(raw);
        if (store is null)
        {
            // Corrupt moddata: never overwrite it with an empty store (that would silently and
            // permanently wipe every snapshot the player had stashed for other dimensions). Skip the
            // swap entirely and leave the raw bytes in place; keep a copy under a recovery key too.
            _sapi.Logger?.Error(
                "[Manifold] Corrupt inventory profile for {0}; skipping inventory swap and preserving raw moddata.",
                player.PlayerName);
            if (raw is { Length: > 0 })
            {
                player.SetModdata(InventoryCorruptModdataKey, raw);
            }

            return;
        }

        var plan = InventoryProfileResolver.Plan(
            targetImpl?.SeparateInventory ?? ManifoldInventory.None,
            target.Code.ToString(),
            store.CurrentKey);

        if (plan.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var swap in plan)
            {
                // Snapshot the current contents into the source key BEFORE touching any slot.
                store.SetSnapshot(swap.Category, swap.FromKey, _inventory.Serialize(player, swap.Category));

                if (store.HasSnapshot(swap.Category, swap.ToKey))
                {
                    _inventory.Restore(player, swap.Category, store.GetSnapshot(swap.Category, swap.ToKey)!);
                }
                else
                {
                    _inventory.Clear(player, swap.Category);
                }

                store.SetCurrentKey(swap.Category, swap.ToKey);
            }
        }
        finally
        {
            // Persist the store even if a category swap threw partway through the plan. Each swap
            // captures the player's current (original) contents into the source key BEFORE mutating
            // any slot, so persisting here keeps those originals recoverable on the next transit. The
            // engine saves the physical inventory independently of this moddata, so skipping the
            // persist on a mid-swap failure would silently and permanently lose the player's items.
            player.SetModdata(InventoryModdataKey, store.ToBytes());
        }
    }

    /// <summary>
    /// Landing position for a non-player entity: per-transit override, else the resolver (default
    /// <see cref="TargetPositionResolvers.SameXZSurfaceY"/>). No spawn-behavior / last-visited (player-only).
    /// </summary>
    private BlockPos ResolveEntityPosition(Entity entity, IDimension target, TransitionOptions options)
    {
        if (options.OverridePosition is { } overridePos)
        {
            // Copy before stamping: SetDimension mutates in place, and overridePos is the caller's
            // own instance (possibly reused elsewhere, e.g. a cached arena spawn).
            return overridePos.Copy().SetDimension(target.InternalId);
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
            // Copy before stamping: SetDimension mutates in place, and overridePos is the caller's
            // own instance (possibly reused elsewhere, e.g. a cached arena spawn).
            return overridePos.Copy().SetDimension(target.InternalId);
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
