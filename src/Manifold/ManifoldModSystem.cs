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

    /// <summary>
    /// Event bus name Atlas pushes synchronously on the game thread after a world-snapshot
    /// restore, after the SaveGame (moddata included) is restored and before any chunk column
    /// reloads. Test-time only: nothing pushes it in production, so carrying the subscription
    /// costs one string comparison per unrelated bus event.
    /// </summary>
    private const string AtlasRollbackRestoredEvent = "atlas:rollback:restored";

    private DimensionPersistence? _persistence;
    private DimensionRegistry? _registry;
    private DimensionGenerator? _generator;
    private StreamingWorldgenDriver? _streamingDriver;
    private GeneratedColumnStore? _generatedColumns;
    private PlayerPositionStore? _positionStore;
    private SaveGameManifestStore? _manifestStore;
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
        var transit = new TransitService(
            _registry,
            api,
            new TransitMovers(new PlayerTeleporter(), new EntityMover(api), new BlockMover(api)),
            TargetPositionResolvers.SameXZSurfaceY,
            _generator,
            _positionStore,
            inventorySwapper);
        transit.PlayerEntered += OnTransitPlayerEntered;

        ServerFacade = new ManifoldServerFacade(_registry, transit, api, _generator);
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
        var transitHandler = new ClientTransitHandler(clientMirror, api.Logger);
        _network.OnClientDimensionAdded += clientMirror.ApplyAdded;
        _network.OnClientDimensionRemoved += clientMirror.ApplyRemoved;
        _network.OnClientManifest += clientMirror.ApplyManifest;
        _network.OnClientPlayerTransited += transitHandler.Handle;

        _network.RegisterClient(api);

        ClientFacade = new ManifoldClientFacade(clientMirror, transitHandler, api.Logger);
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

                    var min = new BlockPos((cx - radius) * ChunkMath.ChunkSize, 0, (cz - radius) * ChunkMath.ChunkSize, dimId);
                    var max = new BlockPos(
                        ((cx + radius) * ChunkMath.ChunkSize) + (ChunkMath.ChunkSize - 1),
                        api.WorldManager.MapSizeY - 1,
                        ((cz + radius) * ChunkMath.ChunkSize) + (ChunkMath.ChunkSize - 1),
                        dimId);

                    // Runtime relight of already-loaded chunks: must push to clients or the
                    // recomputed light is invisible (server-correct, client never re-meshes).
                    bool relit = DimensionGenerator.RelightBlockBounds(api, dimId, min, max, sendToClients: true);
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

    private void OnRegistryDestroyed(object? sender, DimensionDestroyedEventArgs e)
    {
        _network?.BroadcastDimensionRemoved(new DimensionRemovedPacket
        {
            Code = e.Dimension.Code.ToString(),
            InternalId = e.Dimension.InternalId,
        });

        // The engine id is released back to the allocator on removal and may be reused by a later
        // dimension. Drop the destroyed dim's generator state, saved player positions, and generated
        // -column markers so a reused id does not inherit a stale auto-disabled / initialised flag,
        // stale LastVisited coords, or "already generated" markers that would make the new dimension
        // load the old one's chunks instead of running its own worldgen (common now that ephemeral
        // dims reap on empty and ids recycle within a session).
        _generator?.ForgetDimension(e.Dimension.InternalId);
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
    /// (3) reload the generated-columns and player-position stores from the SaveGame blobs.
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

        var manifest = new Dictionary<AssetLocation, ManifestEntry>();
        foreach (var entry in _persistence.LoadOrEmpty())
        {
            manifest[entry.Code] = entry;
        }

        int dropped = 0;
        foreach (var dim in _registry.All)
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

        // A corrupt/tampered entry (out-of-range id, bad code) must not abort the whole seed loop
        // and break the hydrate - log and skip it, matching LoadOrEmpty's drop-silently policy.
        int reseeded = 0;
        foreach (var entry in manifest.Values)
        {
            if (_registry.Get(entry.Code) is not null)
            {
                continue;
            }

            try
            {
                var state = _persistence.Classify(entry);
                _registry.SeedFromManifest(entry, state);
                reseeded++;
            }
            catch (Exception ex)
            {
                Mod.Logger.Warning("[Manifold] Skipped a corrupt manifest entry '{0}': {1}", entry.Code, ex.Message);
            }
        }

        // Restore the persisted set of generated columns so revisits LOAD (preserving player
        // modifications) instead of regenerating over them, and the per-player last positions.
        _generatedColumns.LoadFromBytes(_manifestStore.Read(GeneratedColumnsKey));
        _positionStore.LoadFromBytes(_manifestStore.Read(PlayerPositionsKey), Mod.Logger);

        return (dropped, reseeded);
    }

    /// <summary>Builds the full manifest snapshot (the join-time packet, and the rollback-resync broadcast).</summary>
    private ManifestSnapshotPacket BuildManifestSnapshot() =>
        new() { Dimensions = _registry!.All.Select(DimensionDescriptorMapper.ToDescriptor).ToList() };

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

        var entries = _registry.All
            .Select(dim => new ManifestEntry(dim.Code, dim.InternalId, dim.Lifetime, dim.OwnerModId))
            .ToList();

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
