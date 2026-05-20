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
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
    private GeneratedColumnStore? _generatedColumns;
    private PlayerPositionStore? _positionStore;
    private SaveGameManifestStore? _manifestStore;
    private ManifoldNetworkChannel? _network;
    private ClientDimensionMirror? _clientMirror;
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
        // Manifold registers no block/item/entity/behaviour classes — pure dependency library.
        base.Start(api);
    }

    /// <inheritdoc/>
    public override void StartServerSide(ICoreServerAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        base.StartServerSide(api);

        _harmony = new HarmonyPatcher(Mod.Logger);
        _harmony.Apply();

        if (!_harmony.IsHealthy)
        {
            BuildUnhealthyServerFacade(api);
            Mod.Logger.Error(
                "[Manifold] Disabled — Harmony patches failed at boot. "
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

        _registry = new DimensionRegistry(allocator);

        // Seed manifest BEFORE wiring Created event, so seeded entries don't
        // trigger broadcasts to clients that aren't connected yet anyway.
        foreach (var entry in _persistence.LoadOrEmpty())
        {
            var state = _persistence.Classify(entry);
            _registry.SeedFromManifest(entry, state);
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

        var transit = new TransitService(
            _registry,
            api,
            new PlayerTeleporter(),
            TargetPositionResolvers.SameXZSurfaceY,
            _generator,
            _positionStore);
        transit.PlayerEntered += OnTransitPlayerEntered;

        ServerFacade = new ManifoldServerFacade(_registry, transit, isHealthy: true);
        ManifoldAccess.SetServerResolver(_ => ServerFacade);

        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.PlayerJoin += player => OnPlayerJoin(player, api);
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
        ManifoldAccess.SetServerResolver(null);
        ManifoldAccess.SetClientResolver(null);
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
            new PlayerTeleporter(),
            TargetPositionResolvers.SameXZSurfaceY,
            generator,
            new PlayerPositionStore());
        transit.MarkUnhealthy();
        ServerFacade = new ManifoldServerFacade(registry, transit, isHealthy: false);
        ManifoldAccess.SetServerResolver(_ => ServerFacade);
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
    }

    private void OnTransitPlayerEntered(object? sender, PlayerEnteredDimensionEventArgs e)
    {
        var pos = e.Player.Entity.Pos.AsBlockPos;
        _network?.SendPlayerTransited(e.Player, new PlayerTransitedPacket
        {
            SourceCode = e.SourceDimension.Code.ToString(),
            TargetCode = e.TargetDimension.Code.ToString(),
            TargetX = pos.X,
            TargetY = pos.Y,
            TargetZ = pos.Z,
        });
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
            "[Manifold] Ready — {0} dimensions known ({1} active, {2} quarantined).",
            _registry.All.Count,
            active,
            quarantined);
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        // Remember where the player was so the LastVisited behavior survives logout/restart.
        if (_positionStore is null || player.Entity?.Pos is not { } pos)
        {
            return;
        }

        _positionStore.Record(player.PlayerUID, pos.Dimension, (int)pos.X, (int)pos.Y, (int)pos.Z);
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

        // If the player logs in already inside a custom dimension, pre-generate their region.
        if (_generator is not null && player.Entity?.Pos is { } entityPos)
        {
            int dim = entityPos.Dimension;
            if (dim != 0)
            {
                int cx = (int)entityPos.X / 32;
                int cz = (int)entityPos.Z / 32;
                _generator.EnsureRegion(sapi, dim, cx, cz, player);
            }
        }
    }

    private void OnServerShutdown()
    {
        Dispose();
    }

    private void OnClientPlayerTransited(PlayerTransitedPacket packet)
    {
        if (_clientMirror is null || ClientFacade is null)
        {
            return;
        }

        var source = _clientMirror.Get(new AssetLocation(packet.SourceCode));
        var target = _clientMirror.Get(new AssetLocation(packet.TargetCode));
        if (source is null || target is null)
        {
            return;
        }

        // Player parameter is awkward on the client (we'd need to look up the local IServerPlayer
        // equivalent, but on the client side that's the local EntityPlayer's player handle).
        // For v0 we raise with the local player from the game world; consumers typically filter by code.
        // The IServerPlayer cast is the simplest path — VS exposes ClientPlayer.Player as IServerPlayer
        // on the integrated server only. On a dedicated client we omit the player.
        // To avoid the cast complexity, we don't construct PlayerEnteredDimensionEventArgs here
        // for v0 — instead consumers should subscribe to ClientMirror.Added/Removed for state changes,
        // and use IClientPlayer events for player-local hooks.

        // Future(v1): wire LocalPlayerTransited with a proper player handle if/when needed.
        _ = source;
        _ = target;
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
