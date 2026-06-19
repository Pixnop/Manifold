using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Client;
using Manifold.Api.Events;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Internal;
using Manifold.Internal.HarmonyPatches;
using Manifold.Internal.Networking;
using Manifold.Internal.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold;

/// <summary>
/// Vintage Story <see cref="ModSystem"/> entry point.
/// Single point of orchestration for all internal Manifold services.
/// </summary>
public sealed class ManifoldModSystem : ModSystem
{
    /// <summary>Savegame key for the persisted set of already-generated dimension columns.</summary>
    private const string GeneratedColumnsKey = "manifold:genchunks";

    /// <summary>Savegame key for per-player per-dimension last positions.</summary>
    private const string PlayerPositionsKey = "manifold:lastpos";

    private HarmonyPatcher? _harmony;
    private DimensionPersistence? _persistence;
    private DimensionRegistry? _registry;
    private DimensionGenerator? _generator;
    private StreamingWorldgenDriver? _streamingDriver;
    private GeneratedColumnStore? _generatedColumns;
    private PlayerPositionStore? _positionStore;
    private SaveGameManifestStore? _manifestStore;
    private ManifoldNetworkChannel? _network;
    private ClientDimensionMirror? _clientMirror;
    private ICoreServerAPI? _sapi;
    private bool _disposed;

    /// <summary>Server-side facade. <c>null</c> on the client side or before <c>StartServerSide</c>.</summary>
    internal ManifoldServerFacade? ServerFacade { get; private set; }

    /// <summary>Client-side facade. <c>null</c> on the server side or before <c>StartClientSide</c>.</summary>
    internal ManifoldClientFacade? ClientFacade { get; private set; }

    /// <inheritdoc/>
    public override double ExecuteOrder() => 0.05;

    /// <inheritdoc/>
    public override void Start(ICoreAPI api)
    {
        // Manifold registers no block/item/entity/behaviour classes - pure dependency library.
        base.Start(api);
    }

    /// <inheritdoc/>
    public override void StartServerSide(ICoreServerAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        base.StartServerSide(api);

        _sapi = api;
        _harmony = new HarmonyPatcher(Mod.Logger);
        _harmony.Apply();

        if (!_harmony.IsHealthy)
        {
            BuildUnhealthyServerFacade(api);
            Mod.Logger.Error(
                "[Manifold] Disabled - Harmony patches failed at boot. "
                + "IsHealthy=false; consumer mutations will throw.");
            return;
        }

        var allocator = new DimensionAllocator();
        _manifestStore = new SaveGameManifestStore(api);
        _persistence = new DimensionPersistence(
            _manifestStore,
            new ModLoaderQuery(api.ModLoader));
        _network = new ManifoldNetworkChannel();
        _network.RegisterServer(api);

        // The occupancy predicate makes TryRemove refuse to destroy a dimension a connected player is
        // standing in (ForceRemoveDimension evacuates first to override that).
        _registry = new DimensionRegistry(allocator, IsDimensionOccupied);

        // Seed manifest BEFORE wiring Created event, so seeded entries don't
        // trigger broadcasts to clients that aren't connected yet anyway. A corrupt/tampered entry
        // (out-of-range id, bad code) must not abort the whole seed loop and break Manifold's boot -
        // log and skip it, matching LoadOrEmpty's drop-silently policy for unreadable data.
        foreach (var entry in _persistence.LoadOrEmpty())
        {
            try
            {
                var state = _persistence.Classify(entry);
                _registry.SeedFromManifest(entry, state);
            }
            catch (Exception ex)
            {
                Mod.Logger.Warning("[Manifold] Skipped a corrupt manifest entry '{0}': {1}", entry.Code, ex.Message);
            }
        }

        _registry.Created += OnRegistryCreated;
        _registry.Destroyed += OnRegistryDestroyed;

        // Restore the persisted set of generated columns so revisits LOAD (preserving player
        // modifications) instead of regenerating over them.
        _generatedColumns = new GeneratedColumnStore();
        _generatedColumns.LoadFromBytes(_manifestStore.Read(GeneratedColumnsKey));

        _positionStore = new PlayerPositionStore();
        _positionStore.LoadFromBytes(_manifestStore.Read(PlayerPositionsKey));

        _generator = new DimensionGenerator(_registry, _generatedColumns);
        _generator.StrategyThrew += (dim, _, ex) =>
            Mod.Logger.Error("[Manifold] Worldgen strategy threw for dim {0}: {1}", dim, ex);
        _generator.StrategyAutoDisabled += dim =>
            Mod.Logger.Warning(
                "[Manifold] Worldgen strategy auto-disabled for dim {0} after 4 consecutive throws.", dim);

        var inventorySwapper = new InventorySwapper(api);
        var transit = new TransitService(
            _registry,
            api,
            new TransitMovers(new PlayerTeleporter(), new EntityMover(api), new BlockMover(api)),
            TargetPositionResolvers.SameXZSurfaceY,
            _generator,
            _positionStore,
            inventorySwapper);
        transit.PlayerEntered += OnTransitPlayerEntered;
        transit.PlayerLeft += OnTransitPlayerLeft;

        ServerFacade = new ManifoldServerFacade(_registry, transit, api, isHealthy: true);
        ManifoldAccess.SetServerResolver(_ => ServerFacade);

        RegisterManifoldCommand(api);

        _streamingDriver = new StreamingWorldgenDriver(api, _registry, _generator);
        _streamingDriver.Start();

        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.PlayerJoin += player => OnPlayerJoin(player, api);
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnServerShutdown);
        api.Event.SaveGameLoaded += OnSaveGameLoaded;

        Mod.Logger.Notification("[Manifold] Initialized (healthy).");
    }

    /// <inheritdoc/>
    public override void StartClientSide(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        base.StartClientSide(api);

        _network = new ManifoldNetworkChannel();
        _clientMirror = new ClientDimensionMirror();

        _network.OnClientDimensionAdded += _clientMirror.ApplyAdded;
        _network.OnClientDimensionRemoved += _clientMirror.ApplyRemoved;
        _network.OnClientManifest += _clientMirror.ApplyManifest;
        _network.OnClientPlayerTransited += OnClientPlayerTransited;

        _network.RegisterClient(api);

        ClientFacade = new ManifoldClientFacade(_clientMirror);
        ManifoldAccess.SetClientResolver(_ => ClientFacade);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Clear only the resolver this instance installed. In a singleplayer host the client and
        // server are two ModSystem instances sharing ManifoldAccess's process-global statics; clearing
        // the other side here would strip a still-live facade out from under it.
        if (ServerFacade is not null)
        {
            ManifoldAccess.SetServerResolver(null);
        }

        if (ClientFacade is not null)
        {
            ManifoldAccess.SetClientResolver(null);
        }

        _harmony?.Dispose();
        base.Dispose();
    }

    private void BuildUnhealthyServerFacade(ICoreServerAPI sapi)
    {
        // Allocator + registry + transit are still created so that consumers calling GetManifoldServer()
        // see a coherent (but unhealthy) facade. Transit.MarkUnhealthy ensures TeleportPlayer throws.
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var transit = new TransitService(
            registry,
            sapi,
            new TransitMovers(new PlayerTeleporter(), new EntityMover(sapi), new BlockMover(sapi)),
            TargetPositionResolvers.SameXZSurfaceY,
            generator,
            new PlayerPositionStore(),
            new InventorySwapper(sapi));
        transit.MarkUnhealthy();
        ServerFacade = new ManifoldServerFacade(registry, transit, sapi, isHealthy: false);
        ManifoldAccess.SetServerResolver(_ => ServerFacade);
    }

    /// <summary>
    /// Registers the <c>/manifold</c> admin command. Currently one subcommand:
    /// <c>/manifold relight [radius]</c> relights the chunk columns around the caller in the
    /// dimension they are standing in, over the full world height. Exists because the engine's
    /// own relight paths (including <c>/debug chunk relight</c>) are dimension-blind.
    /// </summary>
    private void RegisterManifoldCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("manifold")
            .WithDescription("Manifold admin utilities.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("relight")
                .WithDescription("Relight the chunks around you in your current dimension (radius in chunks, default 1, max 4).")
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalInt("radius", 1))
                .HandleWith(args =>
                {
                    if (args.Caller.Player is not IServerPlayer player)
                    {
                        return TextCommandResult.Error("Players only.");
                    }

                    int radius = Math.Clamp((int)args[0], 0, 4);
                    var pos = EntityPosAccess.Pos(player.Entity);
                    int dimId = pos.Dimension;
                    int cx = ChunkMath.ToChunk(pos.X);
                    int cz = ChunkMath.ToChunk(pos.Z);

                    var min = new BlockPos((cx - radius) * 32, 0, (cz - radius) * 32, dimId);
                    var max = new BlockPos(
                        ((cx + radius) * 32) + 31,
                        api.WorldManager.MapSizeY - 1,
                        ((cz + radius) * 32) + 31,
                        dimId);

                    // Runtime relight of already-loaded chunks: must push to clients or the
                    // recomputed light is invisible (server-correct, client never re-meshes).
                    DimensionGenerator.RelightBlockBounds(api, dimId, min, max, sendToClients: true);
                    return TextCommandResult.Success(
                        $"Relit dim {dimId}, chunks ({cx - radius},{cz - radius}) to ({cx + radius},{cz + radius}), full height.");
                })
            .EndSubCommand();
    }

    private void OnRegistryCreated(object? sender, DimensionCreatedEventArgs e)
    {
        _network?.BroadcastDimensionAdded(new DimensionAddedPacket
        {
            Dimension = DimensionDescriptorMapper.ToDescriptor(e.Dimension),
        });
    }

    private void OnRegistryDestroyed(object? sender, DimensionDestroyedEventArgs e)
    {
        _network?.BroadcastDimensionRemoved(new DimensionRemovedPacket
        {
            Code = e.Dimension.Code.ToString(),
            InternalId = e.Dimension.InternalId,
        });

        // The engine id is released back to the allocator on removal and may be reused by a later
        // dimension. Drop the destroyed dim's generator state and saved player positions so a reused
        // id does not inherit a stale auto-disabled / initialised flag or stale LastVisited coords
        // (common now that ephemeral dims reap on empty).
        _generator?.ForgetDimension(e.Dimension.InternalId);
        _positionStore?.RemoveDimension(e.Dimension.InternalId);

        // No occupant evacuation here: a dimension is never removed while occupied (TryRemove refuses,
        // ForceRemoveDimension evacuates before removing), so by the time Destroyed fires it is empty.
        // Players whose saved position points to an already-gone dimension are caught at join-time.
    }

    /// <summary>
    /// Sends a player back to the overworld (last-visited position) when they are stranded in a
    /// destroyed/missing/quarantined dimension. Best-effort: a failure is logged, never thrown.
    /// </summary>
    private void RescueToOverworld(IServerPlayer player)
    {
        var transit = ServerFacade?.Transitions;
        if (transit is null)
        {
            return;
        }

        try
        {
            transit.TeleportPlayer(
                player,
                new AssetLocation("manifold", "overworld"),
                new TransitionOptions { SpawnBehavior = SpawnBehavior.LastVisited });
            Mod.Logger.Notification(
                "[Manifold] Rescued {0} to the overworld (their dimension no longer exists).",
                player.PlayerName);
        }
        catch (System.Exception ex)
        {
            Mod.Logger.Warning("[Manifold] Failed to rescue {0} to the overworld: {1}", player.PlayerName, ex.Message);
        }
    }

    /// <summary>
    /// Occupancy predicate handed to the registry: <c>true</c> when a connected player is currently
    /// inside the dimension with the given engine id. <see cref="DimensionRegistry.TryRemove"/> uses
    /// it to refuse removing an occupied dimension.
    /// </summary>
    /// <param name="internalId">Engine dimension id.</param>
    /// <returns><c>true</c> if at least one online player stands in that dimension.</returns>
    private bool IsDimensionOccupied(int internalId)
    {
        // internalId 0 is the overworld; TryRemove throws on it before reaching the occupancy check,
        // so the == 0 guard is belt-and-suspenders (and a safe default if the predicate is reused).
        if (_sapi is null || internalId == 0)
        {
            return false;
        }

        foreach (var p in _sapi.World.AllOnlinePlayers)
        {
            if (p is IServerPlayer sp && EntityPosAccess.PosOrNull(sp.Entity)?.Dimension == internalId)
            {
                return true;
            }
        }

        return false;
    }

    private void OnTransitPlayerEntered(object? sender, PlayerEnteredDimensionEventArgs e)
    {
        var pos = EntityPosAccess.Pos(e.Player.Entity).AsBlockPos;
        _network?.SendPlayerTransited(e.Player, new PlayerTransitedPacket
        {
            SourceCode = e.SourceDimension.Code.ToString(),
            TargetCode = e.TargetDimension.Code.ToString(),
            TargetX = pos.X,
            TargetY = pos.Y,
            TargetZ = pos.Z,
        });
    }

    private void OnTransitPlayerLeft(object? sender, PlayerLeftDimensionEventArgs e)
    {
        // When a player transits out, try to reap the dimension they left if it is an empty ephemeral
        // instance. Disconnect does NOT reap (see OnPlayerDisconnect): a logged-out player keeps their
        // dimension so they reconnect into it; it is only reaped on a deliberate leave or at shutdown.
        ReapEphemeralIfEmpty(e.SourceDimension.InternalId);
    }

    /// <summary>
    /// Reaps (auto-removes) the dimension with the given engine id if it is an empty ephemeral
    /// instance - the natural end of an ephemeral dimension's life when its occupants leave. The
    /// emptiness check is delegated to <see cref="DimensionRegistry.TryRemove"/>, which refuses while
    /// any connected player is still inside, so this never destroys an occupied dimension. No-op for
    /// the overworld or non-ephemeral dimensions.
    /// </summary>
    /// <param name="internalId">Engine id of the dimension the player just left.</param>
    private void ReapEphemeralIfEmpty(int internalId)
    {
        if (_registry is null || internalId == 0)
        {
            return;
        }

        var dim = _registry.GetByInternalId(internalId);
        if (dim is null || dim.Lifetime != DimensionLifetime.Ephemeral)
        {
            return;
        }

        // TryRemove returns false (refuses) while anyone is still inside, so a success here means the
        // dimension was genuinely empty.
        if (_registry.TryRemove(dim.Code))
        {
            Mod.Logger.Notification(
                "[Manifold] Reaped empty ephemeral dimension {0} (last occupant left).", dim.Code);
        }
    }

    private void OnSaveGameLoaded()
    {
        if (_registry is null)
        {
            return;
        }

        int active = CountByState(DimensionState.Active);
        int quarantined = CountByState(DimensionState.Quarantined);
        Mod.Logger.Notification(
            "[Manifold] Ready - {0} dimensions known ({1} active, {2} quarantined).",
            _registry.All.Count,
            active,
            quarantined);
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        // Remember where the player was so the LastVisited behavior survives logout/restart.
        if (_positionStore is null || EntityPosAccess.PosOrNull(player.Entity) is not { } pos)
        {
            return;
        }

        _positionStore.Record(player.PlayerUID, pos.Dimension, (int)pos.X, (int)pos.Y, (int)pos.Z);

        // Deliberately NOT reaping the player's dimension on disconnect: logging out is a pause, not
        // leaving. The dimension is kept so the player reconnects straight back into it (if the server
        // stays up). Ephemeral dimensions are still cleaned up at shutdown, and a deliberate transit
        // out reaps an emptied one. If the server restarts and the ephemeral dim is gone, the join
        // rescue returns the player to the overworld.
    }

    private void OnGameWorldSave()
    {
        if (_persistence is null || _registry is null)
        {
            return;
        }

        var entries = new List<ManifestEntry>();
        foreach (var dim in _registry.All)
        {
            entries.Add(new ManifestEntry(dim.Code, dim.InternalId, dim.Lifetime, dim.OwnerModId));
        }

        _persistence.Save(entries);

        // Persist the generated-columns set so revisits after restart load instead of regenerate.
        if (_generatedColumns is { IsDirty: true } && _manifestStore is not null)
        {
            _manifestStore.Write(GeneratedColumnsKey, _generatedColumns.ToBytes());
            _generatedColumns.ClearDirty();
        }

        // Persist per-player last positions for the LastVisited spawn behavior.
        if (_positionStore is { IsDirty: true } && _manifestStore is not null)
        {
            _manifestStore.Write(PlayerPositionsKey, _positionStore.ToBytes());
            _positionStore.ClearDirty();
        }
    }

    private void OnPlayerJoin(IServerPlayer player, ICoreServerAPI sapi)
    {
        if (_network is null || _registry is null)
        {
            return;
        }

        var snapshot = new ManifestSnapshotPacket();
        foreach (var dim in _registry.All)
        {
            snapshot.Dimensions.Add(DimensionDescriptorMapper.ToDescriptor(dim));
        }

        _network.SendManifestSnapshot(player, snapshot);

        if (EntityPosAccess.PosOrNull(player.Entity) is { } entityPos)
        {
            int dim = entityPos.Dimension;
            if (dim != 0 && _generator is not null)
            {
                var d = _registry.GetByInternalId(dim);
                if (d is { State: DimensionState.Active })
                {
                    // Valid custom dimension: pre-generate their region so they land on solid ground.
                    int cx = ChunkMath.ToChunk(entityPos.X);
                    int cz = ChunkMath.ToChunk(entityPos.Z);
                    _generator.EnsureRegion(sapi, dim, cx, cz, player);
                }

                // A stale/inactive dimension is handled at PlayerNowPlaying: teleporting here (during
                // the connecting screen, before the client entity exists) sends a packet to a null
                // entity and crashes the client.
            }
        }
    }

    /// <summary>
    /// Fires once the player has fully spawned and received their initial chunks - the safe point to
    /// teleport. Rescues players whose saved position points to a dimension that no longer exists or
    /// is not active (ephemeral destroyed at shutdown, owning mod uninstalled, crash) so they are not
    /// stranded in the void.
    /// </summary>
    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        if (_registry is null || EntityPosAccess.PosOrNull(player.Entity) is not { } entityPos)
        {
            return;
        }

        int dim = entityPos.Dimension;
        if (dim == 0)
        {
            return;
        }

        var d = _registry.GetByInternalId(dim);
        if (d is null || d.State != DimensionState.Active)
        {
            RescueToOverworld(player);
        }
    }

    private void OnServerShutdown()
    {
        _streamingDriver?.Stop();

        // Remove every Ephemeral dimension so companions subscribed to Destroyed (or its client
        // mirror) can clean up state owned by the dim (e.g. cached map tiles) before shutdown.
        // The manifest already skips ephemeral entries, so the dim disappears at next boot
        // regardless - this exists purely to fire the Destroyed event for listeners.
        if (_registry is not null)
        {
            int removed = EphemeralCleanup.RemoveAll(_registry);
            if (removed > 0)
            {
                Mod.Logger.Notification(
                    "[Manifold] Shutdown: removed {0} ephemeral dimension(s); Destroyed events fired.",
                    removed);
            }
        }

        Dispose();
    }

    private void OnClientPlayerTransited(PlayerTransitedPacket packet)
    {
        // Reserved scaffolding for IManifoldClient.LocalPlayerTransited (not raised yet - the event
        // args require an IServerPlayer the client does not have; see the event's XML doc). The packet
        // type stays registered so v1 can light up the event without a protocol change. No per-packet
        // work until then: consumers use ClientMirror Added/Removed + IClientPlayer events for now.
    }

    private int CountByState(DimensionState state)
    {
        if (_registry is null)
        {
            return 0;
        }

        int n = 0;
        foreach (var dim in _registry.All)
        {
            if (dim.State == state)
            {
                n++;
            }
        }

        return n;
    }
}
