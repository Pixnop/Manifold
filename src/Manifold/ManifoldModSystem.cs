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
    private HarmonyPatcher? _harmony;
    private DimensionAllocator? _allocator;
    private DimensionPersistence? _persistence;
    private DimensionRegistry? _registry;
    private DimensionGenerator? _generator;
    private TransitService? _transit;
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
    public override void StartServerSide(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        base.StartServerSide(sapi);

        _harmony = new HarmonyPatcher(Mod.Logger);
        _harmony.Apply();

        if (!_harmony.IsHealthy)
        {
            BuildUnhealthyServerFacade(sapi);
            Mod.Logger.Error(
                "[Manifold] Disabled — Harmony patches failed at boot. "
                + "IsHealthy=false; consumer mutations will throw.");
            return;
        }

        _allocator = new DimensionAllocator();
        _persistence = new DimensionPersistence(
            new SaveGameManifestStore(sapi),
            new ModLoaderQuery(sapi.ModLoader));
        _network = new ManifoldNetworkChannel();
        _network.RegisterServer(sapi);

        _registry = new DimensionRegistry(_allocator, () => GuessCallerModId(sapi));

        // Seed manifest BEFORE wiring Created event, so seeded entries don't
        // trigger broadcasts to clients that aren't connected yet anyway.
        foreach (var entry in _persistence.LoadOrEmpty())
        {
            var state = _persistence.Classify(entry);
            _registry.SeedFromManifest(entry, state);
        }

        _registry.Created += OnRegistryCreated;
        _registry.Destroyed += OnRegistryDestroyed;

        _generator = new DimensionGenerator(_registry);
        _generator.StrategyThrew += (dim, _, ex) =>
            Mod.Logger.Error("[Manifold] Worldgen strategy threw for dim {0}: {1}", dim, ex);
        _generator.StrategyAutoDisabled += dim =>
            Mod.Logger.Warning(
                "[Manifold] Worldgen strategy auto-disabled for dim {0} after 4 consecutive throws.", dim);

        _transit = new TransitService(
            _registry,
            sapi,
            new PlayerTeleporter(),
            TargetPositionResolvers.SameXZSurfaceY,
            _generator);
        _transit.PlayerEntered += OnTransitPlayerEntered;

        ServerFacade = new ManifoldServerFacade(_registry, _transit, isHealthy: true);
        ManifoldAccess.SetServerResolver(_ => ServerFacade);

        sapi.Event.GameWorldSave += OnGameWorldSave;
        sapi.Event.PlayerJoin += player => OnPlayerJoin(player, sapi);
        sapi.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnServerShutdown);

        int active = CountByState(DimensionState.Active);
        int quarantined = CountByState(DimensionState.Quarantined);
        Mod.Logger.Notification(
            "[Manifold] Ready — {0} dimensions known ({1} active, {2} quarantined).",
            _registry.All.Count,
            active,
            quarantined);
    }

    /// <inheritdoc/>
    public override void StartClientSide(ICoreClientAPI capi)
    {
        ArgumentNullException.ThrowIfNull(capi);
        base.StartClientSide(capi);

        _network = new ManifoldNetworkChannel();
        _clientMirror = new ClientDimensionMirror();

        _network.OnClientDimensionAdded += _clientMirror.ApplyAdded;
        _network.OnClientDimensionRemoved += _clientMirror.ApplyRemoved;
        _network.OnClientManifest += _clientMirror.ApplyManifest;
        _network.OnClientPlayerTransited += OnClientPlayerTransited;

        _network.RegisterClient(capi);

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
        var registry = new DimensionRegistry(allocator, () => Mod.Info.ModID);
        var generator = new DimensionGenerator(registry);
        var transit = new TransitService(
            registry,
            sapi,
            new PlayerTeleporter(),
            TargetPositionResolvers.SameXZSurfaceY,
            generator);
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

        // TODO(v1): wire LocalPlayerTransited with a proper player handle if/when needed.
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

    private string GuessCallerModId(ICoreServerAPI sapi)
    {
        // Best-effort: walk the stack, find the first frame outside Manifold/System/Vintagestory namespaces.
        // Match the frame's assembly name to a loaded mod's primary assembly to extract its modid.
        var stack = new System.Diagnostics.StackTrace(false);
        foreach (var frame in stack.GetFrames())
        {
            var method = frame.GetMethod();
            if (method?.DeclaringType is null)
            {
                continue;
            }

            var ns = method.DeclaringType.Namespace ?? string.Empty;
            if (ns.StartsWith("Manifold", StringComparison.Ordinal) ||
                ns.StartsWith("System", StringComparison.Ordinal) ||
                ns.StartsWith("Vintagestory", StringComparison.Ordinal))
            {
                continue;
            }

            var asmName = method.DeclaringType.Assembly.GetName().Name ?? string.Empty;
            foreach (var mod in sapi.ModLoader.Mods)
            {
                if (mod.Systems is { Count: > 0 } systems)
                {
                    var firstSystem = systems.First();
                    var modAsm = firstSystem.GetType().Assembly.GetName().Name ?? string.Empty;
                    if (string.Equals(asmName, modAsm, StringComparison.Ordinal))
                    {
                        return mod.Info.ModID;
                    }
                }
            }

            // Fallback: return the assembly name as a synthetic mod id.
            return asmName;
        }

        return Mod.Info.ModID;
    }
}
