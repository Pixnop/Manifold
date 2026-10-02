using System;
using System.Collections.Generic;
using System.Linq;
using Manifold.Api;
using Manifold.Api.Client;
using Manifold.Api.Events;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Internal;
using Manifold.Internal.Networking;
using Manifold.Internal.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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

    /// <summary>Savegame key for per-player per-dimension origins (where the player came from), added in 0.6.1.</summary>
    private const string PlayerOriginsKey = "manifold:origins";

    /// <summary>
    /// Event bus name Atlas pushes synchronously on the game thread after a world-snapshot
    /// restore, after the SaveGame (moddata included) is restored and before any chunk column
    /// reloads. Test-time only: nothing pushes it in production, so carrying the subscription
    /// costs one string comparison per unrelated bus event.
    /// </summary>
    private const string AtlasRollbackRestoredEvent = "atlas:rollback:restored";

    /// <summary>
    /// How often pending relights are looked at again: short enough that light restored near a
    /// player reaches clients within about half a second, and idle when nothing is pending.
    /// </summary>
    private const int RelightRetryIntervalMs = 250;

    private DimensionPersistence? _persistence;
    private DimensionRegistry? _registry;
    private DimensionGenerator? _generator;
    private BlockLightRestorer? _lightRestorer;
    private StreamingWorldgenDriver? _streamingDriver;
    private GeneratedColumnStore? _generatedColumns;
    private PlayerPositionStore? _positionStore;
    private PlayerTeleporter? _playerTeleporter;
    private SaveGameManifestStore? _manifestStore;
    private SchemaSidecar? _schemaSidecar;
    private ManifoldNetworkChannel? _network;
    private ICoreServerAPI? _sapi;
    private bool _disposed;

    /// <summary>Server-side facade. <c>null</c> on the client side or before <c>StartServerSide</c>.</summary>
    internal ManifoldServerFacade? ServerFacade { get; private set; }

    /// <summary>Client-side facade. <c>null</c> on the server side or before <c>StartClientSide</c>.</summary>
    internal ManifoldClientFacade? ClientFacade { get; private set; }

    /// <inheritdoc/>
    public override double ExecuteOrder() => 0.05;

    /// <inheritdoc/>
    public override void StartServerSide(ICoreServerAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        base.StartServerSide(api);

        _sapi = api;

        string? gameVersion = BrokenInteractionVersions.RunningGameVersion();
        if (BrokenInteractionVersions.IsAffected(gameVersion))
        {
            Mod.Logger.Warning(
                "Vintage Story {0} cannot interact with blocks outside the overworld: containers close as soon as they open "
                + "and block interactions are refused in every custom dimension. This is a game bug fixed in 1.22.6; "
                + "update the game to 1.22.6 or later.",
                gameVersion);
        }

        var allocator = new DimensionAllocator();
        _manifestStore = new SaveGameManifestStore(api);
        _persistence = new DimensionPersistence(
            _manifestStore,
            api.ModLoader.IsModEnabled,
            Mod.Logger);
        _network = new ManifoldNetworkChannel();
        _network.RegisterServer(api);

        // The occupancy predicate makes TryRemove refuse to destroy a dimension a connected player is
        // standing in (ForceRemoveDimension evacuates first to override that). The logger lets the
        // registry report (and swallow) exceptions thrown by third-party event subscribers.
        _registry = new DimensionRegistry(allocator, IsDimensionOccupied, Mod.Logger);

        _generatedColumns = new GeneratedColumnStore();
        _positionStore = new PlayerPositionStore();

        // Hydrate from the SaveGame before wiring Created/Destroyed (also re-run on Atlas rollback, see OnAtlasRollbackRestored).
        ResyncFromSaveGame();

        _registry.Created += OnRegistryCreated;
        _registry.Destroyed += OnRegistryDestroyed;

        _generator = new DimensionGenerator(_registry, _generatedColumns);
        _generator.StrategyThrew += (dim, _, ex) =>
            Mod.Logger.Error("[Manifold] Worldgen strategy threw for dim {0}: {1}", dim, ex);
        _generator.StrategyAutoDisabled += dim =>
            Mod.Logger.Warning(
                "[Manifold] Worldgen strategy auto-disabled for dim {0} after {1} consecutive throws.",
                dim,
                DimensionGenerator.MaxConsecutiveFailures);

        var inventorySwapper = new InventorySwapper(api);
        _playerTeleporter = new PlayerTeleporter();
        var transit = new TransitService(
            _registry,
            api,
            new TransitMovers(_playerTeleporter, new EntityMover(api), new BlockMover(api), new PlayerDismounter(api)),
            TargetPositionResolvers.SameXZSurfaceY,
            _generator,
            _positionStore,
            inventorySwapper);
        transit.PlayerEntered += OnTransitPlayerEntered;

        _lightRestorer = new BlockLightRestorer(new EngineRelight(api), () => api.World.ElapsedMilliseconds);
        api.Event.RegisterGameTickListener(
            _ => _lightRestorer.Tick(),
            ex => Mod.Logger.Warning("[Manifold] Pending relight pass failed: {0}", ex),
            RelightRetryIntervalMs);

        ServerFacade = new ManifoldServerFacade(_registry, transit, api, _generator, _lightRestorer);
        ManifoldAccess.SetServerResolver(ServerFacade);

        RegisterManifoldCommand(api);

        _streamingDriver = new StreamingWorldgenDriver(api, _registry, _generator);
        _streamingDriver.Start();

        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.PlayerJoin += player => OnPlayerJoin(player, api);
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnServerShutdown);
        api.Event.SaveGameLoaded += OnSaveGameLoaded;

        // Priority 0.6 > default 0.5: Manifold resyncs before consumer mods' restored-handlers (mirrors ExecuteOrder at boot).
        api.Event.RegisterEventBusListener(OnAtlasRollbackRestored, 0.6, AtlasRollbackRestoredEvent);

        Mod.Logger.Notification("[Manifold] Initialized.");
    }

    /// <inheritdoc/>
    public override void StartClientSide(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        base.StartClientSide(api);

        _network = new ManifoldNetworkChannel();

        // The mirror and transit handler are kept alive by the channel delegates and ClientFacade
        // below; neither needs a field.
        var clientMirror = new ClientDimensionMirror(api.Logger);
        var transitHandler = new ClientTransitHandler(clientMirror, api.Logger, yaw => ApplyLocalPlayerYaw(api, yaw));
        var joinNotifier = new ClientJoinNotifier(
            clientMirror,
            () => EntityPosAccess.PosOrNull(api.World.Player?.Entity),
            api.Logger);
        _network.OnClientDimensionAdded += clientMirror.ApplyAdded;
        _network.OnClientDimensionRemoved += clientMirror.ApplyRemoved;
        _network.OnClientManifest += clientMirror.ApplyManifest;
        _network.OnClientManifest += _ => joinNotifier.Notify();
        _network.OnClientPlayerTransited += transitHandler.Handle;

        // "The local player entity exists with its position": the engine raises PlayerEntitySpawn when
        // an entity-player is attached to its client player, whichever of the entity packet and the
        // player-data packet arrives first. It fires for every player's entity, so the notifier
        // itself checks that the LOCAL entity is there (and ignores every call once decided).
        api.Event.PlayerEntitySpawn += _ => joinNotifier.Notify();

        _network.RegisterClient(api);

        ClientFacade = new ManifoldClientFacade(clientMirror, transitHandler, joinNotifier, api.Logger);
        ManifoldAccess.SetClientResolver(ClientFacade);
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

        base.Dispose();
    }

    /// <summary>
    /// Evacuates every player in <paramref name="occupants"/> via <paramref name="rescue"/>
    /// (best-effort per player - a failure is logged, not thrown) and reports how many actually left.
    /// A player <paramref name="rescue"/> silently failed to move is still standing in the dimension
    /// afterward, so it is counted as remaining, not evacuated - the caller must not destroy the
    /// dimension out from under them.
    /// </summary>
    /// <param name="occupants">
    /// Players already known to be inside <paramref name="internalId"/> (e.g. from
    /// <see cref="OccupancyScan.PlayersIn"/>); this does not filter them itself.
    /// </param>
    /// <param name="internalId">Engine id of the dimension being evacuated.</param>
    /// <param name="rescue">Best-effort teleport-to-overworld for one occupant.</param>
    /// <returns>How many occupants were actually moved out, and how many are still inside.</returns>
    internal static (int Evacuated, int Remaining) EvacuateOccupants(
        IEnumerable<IServerPlayer> occupants, int internalId, Action<IServerPlayer> rescue)
    {
        int evacuated = 0;
        int remaining = 0;
        foreach (var p in occupants)
        {
            rescue(p);
            if (OccupancyScan.IsIn(p, internalId))
            {
                remaining++;
            }
            else
            {
                evacuated++;
            }
        }

        return (evacuated, remaining);
    }

    /// <summary>
    /// Records where a disconnecting player is, for the <see cref="Manifold.Api.Transitions.SpawnBehavior.LastVisited"/>
    /// behavior, then forgets any teleport still waiting for them. A player who disconnects while the
    /// engine still has a teleport queued reports the new dimension but the coordinates they left, so
    /// the landing that teleport asked for is what is recorded; and the completion the engine may still
    /// run for their old entity must not apply anything afterwards.
    /// </summary>
    /// <param name="positionStore">Store to record the position in.</param>
    /// <param name="teleporter">Teleporter that knows the pending landing.</param>
    /// <param name="player">The disconnecting player.</param>
    internal static void RecordDisconnectPosition(PlayerPositionStore positionStore, IPlayerTeleporter teleporter, IServerPlayer player)
    {
        var pending = teleporter.GetPendingLanding(player);
        teleporter.Forget(player);
        if (EntityPosAccess.PosOrNull(player.Entity) is not { } pos)
        {
            return;
        }

        var (x, y, z) = pending is { } landing ? (landing.X, landing.Y, landing.Z) : (pos.X, pos.Y, pos.Z);
        positionStore.Record(player.PlayerUID, pos.Dimension, (int)x, (int)y, (int)z);
    }

    /// <summary>
    /// Persists the manifest and whichever savegame-level stores changed, then the sidecar itself.
    /// Pulled out of <see cref="OnGameWorldSave"/>, taking every dependency as a parameter instead
    /// of reading instance fields, so the refusal guards below are unit-testable without a real
    /// <see cref="ICoreServerAPI"/>.
    /// </summary>
    /// <param name="manifestStore">Savegame byte store.</param>
    /// <param name="persistence">Dimension manifest reader/writer.</param>
    /// <param name="entries">Current in-memory manifest entries to persist.</param>
    /// <param name="generatedColumns">Generated-columns store to persist if dirty.</param>
    /// <param name="positionStore">Per-player last-position store to persist if dirty.</param>
    /// <param name="sidecar">Schema sidecar to update and persist.</param>
    internal static void SaveWorldState(
        IManifestStore manifestStore,
        DimensionPersistence persistence,
        IEnumerable<ManifestEntry> entries,
        GeneratedColumnStore generatedColumns,
        PlayerPositionStore positionStore,
        SchemaSidecar sidecar)
    {
        persistence.Save(entries);
        if (!persistence.IsVersionRefused)
        {
            sidecar.SetVersion(DimensionPersistence.ManifestKey, DimensionPersistence.SchemaVersion);
        }

        // Persist the generated-columns set so revisits after restart load instead of regenerate.
        // Skipped while IsVersionRefused: a newer, unrecognized-version blob is on disk (preserved
        // under its own ".unrecognized" key), and this session's set is not a reliable replacement
        // for it (its sidecar entry is left exactly as loaded too, never downgraded).
        if (generatedColumns is { IsDirty: true, IsVersionRefused: false })
        {
            manifestStore.Write(GeneratedColumnsKey, generatedColumns.ToBytes());
            generatedColumns.ClearDirty();
            sidecar.SetVersion(GeneratedColumnsKey, GeneratedColumnStore.SchemaVersion);
        }

        // Persist per-player last positions for the LastVisited spawn behavior. Same refusal skip.
        if (positionStore is { IsDirty: true, IsVersionRefused: false })
        {
            manifestStore.Write(PlayerPositionsKey, positionStore.ToBytes());
            positionStore.ClearDirty();
            sidecar.SetVersion(PlayerPositionsKey, PlayerPositionStore.SchemaVersion);
        }

        // Persist where each player came from (the return-to-origin memory). Its own key and version,
        // so a build that predates it simply ignores the key. Same refusal skip.
        if (positionStore.Origins is { IsDirty: true, IsVersionRefused: false } origins)
        {
            manifestStore.Write(PlayerOriginsKey, origins.ToBytes());
            origins.ClearDirty();
            sidecar.SetVersion(PlayerOriginsKey, PlayerOriginStore.SchemaVersion);
        }

        // Written whenever any of the blobs above is (re-)written, so the sidecar always matches
        // what is actually on disk for each key it names; also keeps a refused key's entry exactly
        // as loaded, since only the branches above ever advance it.
        manifestStore.Write(SchemaSidecar.Key, sidecar.ToBytes());
    }

    /// <summary>
    /// Registers the <c>/manifold</c> admin command. Currently one subcommand:
    /// <c>/manifold relight [radius]</c> relights the chunk columns around the caller in the
    /// dimension they are standing in, over the full world height. Exists because the engine's
    /// own relight paths (including <c>/debug chunk relight</c>) are dimension-blind. Same
    /// behaviour as <c>IManifoldServer.RelightRegion</c>: sunlight at once, block light restored
    /// through the engine's lighting queue a moment later.
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

                    var min = new BlockPos((cx - radius) * ChunkMath.ChunkSize, 0, (cz - radius) * ChunkMath.ChunkSize, dimId);
                    var max = new BlockPos(
                        ((cx + radius) * ChunkMath.ChunkSize) + (ChunkMath.ChunkSize - 1),
                        api.WorldManager.MapSizeY - 1,
                        ((cz + radius) * ChunkMath.ChunkSize) + (ChunkMath.ChunkSize - 1),
                        dimId);

                    // Runtime relight of already-loaded chunks: must resend to clients or the
                    // recomputed light is invisible (server-correct, client never re-meshes).
                    bool relit = _lightRestorer!.Relight(dimId, min, max, sendToClients: true);
                    return relit
                        ? TextCommandResult.Success(
                            $"Relit dim {dimId}, chunks ({cx - radius},{cz - radius}) to ({cx + radius},{cz + radius}), full height.")
                        : TextCommandResult.Error($"Relight of dim {dimId} failed; see the server log.");
                })
            .EndSubCommand()
            .BeginSubCommand("purge")
                .WithDescription(
                    "Force-remove a dimension by code, releasing its engine id. Evacuates any occupants "
                    + "to the overworld first. Use for a dimension whose owning mod was uninstalled (Quarantined).")
                .WithArgs(api.ChatCommands.Parsers.Word("code"))
                .HandleWith(args => PurgeCommand(api, args))
            .EndSubCommand();
    }

    /// <summary>
    /// Handles <c>/manifold purge &lt;code&gt;</c>: evacuates any occupants of the named dimension to
    /// the overworld, then force-removes it (releasing its engine id and firing <c>Destroyed</c>, which
    /// drives the per-dimension cleanup). The documented recovery path for a Quarantined dimension and
    /// the admin teardown for a Persistent one - both of which the plain remove path refuses.
    /// </summary>
    private TextCommandResult PurgeCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (_registry is null)
        {
            return TextCommandResult.Error("Manifold is not initialized.");
        }

        AssetLocation code;
        try
        {
            code = new AssetLocation((args[0] as string) ?? string.Empty);
        }
        catch (Exception)
        {
            return TextCommandResult.Error($"Invalid dimension code '{args[0]}'.");
        }

        var dim = _registry.Get(code);
        if (dim is null)
        {
            return TextCommandResult.Error($"No dimension registered with code '{code}'.");
        }

        if (dim.IsBuiltIn || dim.Lifetime == DimensionLifetime.BuiltIn)
        {
            // Check before evacuating: the built-in overworld is where evacuation itself sends
            // occupants, so evacuating "into" it is a no-op that would otherwise look like a stuck
            // player and report the wrong error instead of this one.
            return TextCommandResult.Error($"Dimension '{code}' is built-in and cannot be purged.");
        }

        // Evacuate anyone standing in the dimension before destroying it, so no one is stranded.
        var (evacuated, remaining) = EvacuateOccupants(
            OccupancyScan.PlayersIn(api, dim.InternalId), dim.InternalId, RescueToOverworld);
        if (remaining > 0)
        {
            return TextCommandResult.Error(
                $"Could not purge dimension '{code}': {remaining} player(s) still inside after the evacuation attempt.");
        }

        // The built-in check above already filters the only case Purge itself throws for.
        _registry.Purge(code);

        string suffix = evacuated > 0 ? $", evacuated {evacuated} player(s)" : string.Empty;
        return TextCommandResult.Success(
            $"Purged dimension '{code}' (engine id {dim.InternalId} released){suffix}.");
    }

    private void OnRegistryCreated(object? sender, DimensionCreatedEventArgs e)
    {
        _network?.BroadcastDimensionAdded(new DimensionAddedPacket
        {
            Dimension = DimensionDescriptorMapper.ToDescriptor(e.Dimension),
        });
    }

    /// <summary>
    /// Turns the local player's camera (and entity) to <paramref name="yaw"/>. The camera is driven
    /// by the client's own mouse state, so setting the entity's yaw alone would be overwritten on the
    /// next frame: <c>IClientPlayer.CameraYaw</c> is the public way to move it.
    /// </summary>
    private static void ApplyLocalPlayerYaw(ICoreClientAPI capi, float yaw)
    {
        var player = capi.World.Player;
        if (player?.Entity is null)
        {
            return;
        }

        player.CameraYaw = yaw;
        EntityPosAccess.Pos(player.Entity).Yaw = yaw;
    }

    private void OnRegistryDestroyed(object? sender, DimensionDestroyedEventArgs e)
    {
        _network?.BroadcastDimensionRemoved(new DimensionRemovedPacket
        {
            Code = e.Dimension.Code.ToString(),
            InternalId = e.Dimension.InternalId,
        });

        // The engine id is released back to the allocator on removal and may be reused by a later
        // dimension. Drop the destroyed dim's generator state, saved player positions (and the origins
        // recorded for it or pointing to it), and generated-column markers so a reused id does not
        // inherit a stale auto-disabled / initialised flag, stale LastVisited coords or origins, or "already generated" markers that would make the new dimension
        // load the old one's chunks instead of running its own worldgen (common now that ephemeral
        // dims reap on empty and ids recycle within a session).
        _generator?.ForgetDimension(e.Dimension.InternalId);
        _lightRestorer?.ForgetDimension(e.Dimension.InternalId);
        _positionStore?.RemoveDimension(e.Dimension.InternalId);
        _generatedColumns?.RemoveDimension(e.Dimension.InternalId);

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

        if (OverworldRescue.TryEvacuate(transit, player, Mod.Logger))
        {
            Mod.Logger.Notification(
                "[Manifold] Rescued {0} to the overworld (their dimension no longer exists).",
                player.PlayerName);
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
        return _sapi is not null && internalId != 0 && OccupancyScan.IsOccupied(_sapi, internalId);
    }

    private void OnTransitPlayerEntered(object? sender, PlayerEnteredDimensionEventArgs e)
    {
        // Send the transit packet before reaping the source dimension below: the client resolves
        // this packet's source/target codes through its dimension mirror, and reaping broadcasts a
        // DimensionRemovedPacket that would otherwise remove the source from that mirror first,
        // making the transit unresolvable on the client (see ReapEphemeralIfEmpty).
        _network?.SendPlayerTransited(e.Player, new PlayerTransitedPacket
        {
            SourceCode = e.SourceDimension.Code.ToString(),
            TargetCode = e.TargetDimension.Code.ToString(),
            TargetX = e.TargetPosition.X,
            TargetY = e.TargetPosition.Y,
            TargetZ = e.TargetPosition.Z,
            Yaw = e.Yaw,
        });

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

    /// <summary>
    /// Handles the Atlas <c>atlas:rollback:restored</c> cooperation event: the world database and
    /// the live SaveGame (manifest included) have just been restored to the captured state, no
    /// chunk column is loaded yet, and this runs synchronously on the game thread. Re-running the
    /// boot hydrate resynchronizes the in-memory registry, allocator, and persisted-store mirrors
    /// with the restored SaveGame. Exceptions deliberately propagate: Atlas classifies a throwing
    /// handler as ModHookFailed and degrades the rollback fail-closed to a full recycle.
    /// </summary>
    private void OnAtlasRollbackRestored(string eventName, ref EnumHandling handling, IAttribute data)
    {
        (int dropped, int reseeded) = ResyncFromSaveGame();

        // Connected clients mirror the registry; after a resync their mirror may describe dropped
        // or missing dimensions. Send the same full snapshot a joining player gets. The drop pass
        // already broadcast per-dimension removals via Destroyed; the snapshot makes the mirror
        // authoritative in one message regardless.
        if (_network is not null && _registry is not null && _sapi is not null)
        {
            var snapshot = BuildManifestSnapshot();
            foreach (var p in _sapi.World.AllOnlinePlayers)
            {
                if (p is IServerPlayer sp)
                {
                    _network.SendManifestSnapshot(sp, snapshot);
                }
            }
        }

        var payload = data as ITreeAttribute;
        Mod.Logger.Notification(
            "[Manifold] Rollback resync (generation {0}, restore #{1}): dropped {2}, reseeded {3} dimension(s); stores reloaded.",
            payload?.GetInt("generation") ?? -1,
            payload?.GetInt("restoreCount") ?? -1,
            dropped,
            reseeded);
    }

    /// <summary>
    /// The re-runnable hydrate: reconciles all in-memory state that mirrors SaveGame data with
    /// whatever the SaveGame currently holds. Called at boot (where the drop pass is a no-op) and
    /// from <see cref="OnAtlasRollbackRestored"/> after a test-harness rollback rewrote the
    /// SaveGame. Three passes:
    /// (1) drop every non-built-in registration the manifest does not describe with the same
    /// (code, id, lifetime, owner), via the purge path so ids are released, Destroyed fires, and
    /// the per-dimension cleanup runs - this forgets ephemeral/persistent dimensions created after
    /// the capture, including id reservations and generator state;
    /// (2) re-seed manifest entries memory lacks (a removal undone by the rollback), Pending or
    /// Quarantined exactly as at boot;
    /// (3) reload the generated-columns and player-position stores from the SaveGame blobs, each
    /// guided by the schema version <see cref="SchemaSidecar"/> records for it.
    /// Registrations matching the manifest keep their in-memory record untouched: it carries
    /// worldgen configuration the manifest does not persist, and an owner-promoted Active state
    /// remains coherent with the restored world.
    /// </summary>
    /// <returns>Counts of dropped and re-seeded registrations, for the caller's log line.</returns>
    private (int Dropped, int Reseeded) ResyncFromSaveGame()
    {
        if (_registry is null || _persistence is null || _manifestStore is null
            || _generatedColumns is null || _positionStore is null)
        {
            return (0, 0);
        }

        _schemaSidecar = SchemaSidecar.Load(_manifestStore.Read(SchemaSidecar.Key));

        var manifest = new Dictionary<AssetLocation, ManifestEntry>();
        foreach (var entry in _persistence.LoadOrEmpty(_schemaSidecar.GetVersion(DimensionPersistence.ManifestKey)))
        {
            manifest[entry.Code] = entry;
        }

        int dropped = DropStaleRegistrations(manifest);
        int reseeded = ReseedMissingManifestEntries(manifest);

        // Restore the persisted set of generated columns so revisits LOAD (preserving player
        // modifications) instead of regenerating over them, and the per-player last positions.
        LoadStoreOrPreserveUnrecognized(_generatedColumns, GeneratedColumnsKey);
        LoadStoreOrPreserveUnrecognized(_positionStore, PlayerPositionsKey);
        LoadStoreOrPreserveUnrecognized(_positionStore.Origins, PlayerOriginsKey);

        return (dropped, reseeded);
    }

    /// <summary>
    /// Loads a savegame-level store's blob under the version <see cref="_schemaSidecar"/> records
    /// for its key, and if the load refuses an unrecognized newer version, logs it (naming the key
    /// and both versions) and copies the raw blob to <c>"{key}.unrecognized"</c> so it is never
    /// lost (the store only latches its own <c>IsVersionRefused</c> flag; it does not hold the key
    /// it was read from or log with it, so that is done here instead).
    /// </summary>
    private void LoadStoreOrPreserveUnrecognized(GeneratedColumnStore store, string key)
    {
        int version = _schemaSidecar!.GetVersion(key);
        var raw = _manifestStore!.Read(key);
        store.LoadFromBytes(raw, version);
        if (store.IsVersionRefused)
        {
            PreserveUnrecognized(key, raw, version, GeneratedColumnStore.SchemaVersion);
        }
    }

    /// <summary>See <see cref="LoadStoreOrPreserveUnrecognized(GeneratedColumnStore, string)"/>.</summary>
    private void LoadStoreOrPreserveUnrecognized(PlayerPositionStore store, string key)
    {
        int version = _schemaSidecar!.GetVersion(key);
        var raw = _manifestStore!.Read(key);
        store.LoadFromBytes(raw, Mod.Logger, version);
        if (store.IsVersionRefused)
        {
            PreserveUnrecognized(key, raw, version, PlayerPositionStore.SchemaVersion);
        }
    }

    /// <summary>See <see cref="LoadStoreOrPreserveUnrecognized(GeneratedColumnStore, string)"/>.</summary>
    private void LoadStoreOrPreserveUnrecognized(PlayerOriginStore store, string key)
    {
        int version = _schemaSidecar!.GetVersion(key);
        var raw = _manifestStore!.Read(key);
        store.LoadFromBytes(raw, Mod.Logger, version);
        if (store.IsVersionRefused)
        {
            PreserveUnrecognized(key, raw, version, PlayerOriginStore.SchemaVersion);
        }
    }

    /// <summary>Logs an unrecognized-schema-version refusal naming the key and both versions, and copies the raw blob to a recovery key.</summary>
    private void PreserveUnrecognized(string key, byte[]? raw, int version, int supported)
    {
        Mod.Logger.Error(
            "[Manifold] '{0}' is schema version {1}, this build supports up to {2}. The original blob is preserved under '{0}.unrecognized' and this key will not be re-saved.",
            key,
            version,
            supported);
        if (raw is { Length: > 0 })
        {
            _manifestStore!.Write(key + ".unrecognized", raw);
        }
    }

    /// <summary>
    /// Drops every non-built-in registration <paramref name="manifest"/> does not describe with the
    /// same (code, id, lifetime, owner), via the purge path so ids are released, Destroyed fires, and
    /// the per-dimension cleanup runs. Part of <see cref="ResyncFromSaveGame"/>'s first pass.
    /// </summary>
    private int DropStaleRegistrations(Dictionary<AssetLocation, ManifestEntry> manifest)
    {
        int dropped = 0;
        foreach (var dim in _registry!.All)
        {
            if (dim.IsBuiltIn || dim.Lifetime == DimensionLifetime.BuiltIn)
            {
                continue;
            }

            if (manifest.TryGetValue(dim.Code, out var entry)
                && entry.InternalId == dim.InternalId
                && entry.Lifetime == dim.Lifetime
                && string.Equals(entry.OwnerModId, dim.OwnerModId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_registry.Purge(dim.Code))
            {
                dropped++;
            }
        }

        return dropped;
    }

    /// <summary>
    /// Re-seeds <paramref name="manifest"/> entries memory lacks (a removal undone by the rollback),
    /// Pending or Quarantined exactly as at boot. A corrupt/tampered entry (out-of-range id, bad code)
    /// must not abort the loop - it is logged and skipped, matching LoadOrEmpty's drop-silently policy.
    /// Part of <see cref="ResyncFromSaveGame"/>'s second pass.
    /// </summary>
    private int ReseedMissingManifestEntries(Dictionary<AssetLocation, ManifestEntry> manifest)
    {
        int reseeded = 0;
        foreach (var entry in manifest.Values)
        {
            if (_registry!.Get(entry.Code) is not null)
            {
                continue;
            }

            try
            {
                var state = _persistence!.Classify(entry);
                _registry.SeedFromManifest(entry, state);
                reseeded++;
            }
            catch (Exception ex)
            {
                Mod.Logger.Warning("[Manifold] Skipped a corrupt manifest entry '{0}': {1}", entry.Code, ex.Message);
            }
        }

        return reseeded;
    }

    /// <summary>Builds the full manifest snapshot (the join-time packet, and the rollback-resync broadcast).</summary>
    private ManifestSnapshotPacket BuildManifestSnapshot() =>
        new() { Dimensions = _registry!.All.Select(DimensionDescriptorMapper.ToDescriptor).ToList() };

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        // Remember where the player was so the LastVisited behavior survives logout/restart.
        if (_positionStore is not null && _playerTeleporter is not null)
        {
            RecordDisconnectPosition(_positionStore, _playerTeleporter, player);
        }

        // Deliberately NOT reaping the player's dimension on disconnect: logging out is a pause, not
        // leaving. The dimension is kept so the player reconnects straight back into it (if the server
        // stays up). Ephemeral dimensions are still cleaned up at shutdown, and a deliberate transit
        // out reaps an emptied one. If the server restarts and the ephemeral dim is gone, the join
        // rescue returns the player to the overworld.
    }

    private void OnGameWorldSave()
    {
        if (_persistence is null || _registry is null || _manifestStore is null
            || _generatedColumns is null || _positionStore is null || _schemaSidecar is null)
        {
            return;
        }

        var entries = _registry.All
            .Select(dim => new ManifestEntry(dim.Code, dim.InternalId, dim.Lifetime, dim.OwnerModId))
            .ToList();

        SaveWorldState(_manifestStore, _persistence, entries, _generatedColumns, _positionStore, _schemaSidecar);
    }

    private void OnPlayerJoin(IServerPlayer player, ICoreServerAPI sapi)
    {
        if (_network is null || _registry is null)
        {
            return;
        }

        _network.SendManifestSnapshot(player, BuildManifestSnapshot());

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

    private int CountByState(DimensionState state) =>
        _registry?.All.Count(dim => dim.State == state) ?? 0;
}
