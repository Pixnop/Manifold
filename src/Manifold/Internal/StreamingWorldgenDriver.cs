using System;
using System.Collections.Generic;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Drives streaming worldgen: on a periodic server tick, gathers online players in streaming
/// dimensions, asks <see cref="StreamingPlanner"/> which columns to ensure (budget-capped,
/// nearest-first), then generates/loads, force-sends, and relights them.
/// </summary>
/// <remarks>Server-side, main thread. Logic lives in <see cref="StreamingPlanner"/>; this is glue.</remarks>
internal sealed class StreamingWorldgenDriver
{
    private const int BudgetPerTick = 4;
    private const int TickIntervalMs = 250;

    private readonly ICoreServerAPI _sapi;
    private readonly DimensionRegistry _registry;
    private readonly DimensionGenerator _generator;
    private long _listenerId = -1;

    /// <summary>Initializes a new instance of the <see cref="StreamingWorldgenDriver"/> class.</summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="registry">Dimension registry (to find streaming dimensions).</param>
    /// <param name="generator">Generator used to ensure individual columns.</param>
    public StreamingWorldgenDriver(ICoreServerAPI sapi, DimensionRegistry registry, DimensionGenerator generator)
    {
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    /// <summary>Registers the periodic tick listener.</summary>
    public void Start() => _listenerId = _sapi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);

    /// <summary>Unregisters the tick listener.</summary>
    public void Stop()
    {
        if (_listenerId >= 0)
        {
            _sapi.Event.UnregisterGameTickListener(_listenerId);
            _listenerId = -1;
        }
    }

    private void OnTick(float dt)
    {
        var players = GatherStreamingPlayers(out var byUid);
        if (players.Count == 0)
        {
            return;
        }

        var planned = StreamingPlanner.Plan(players, IsColumnLoaded, BudgetPerTick);
        if (planned.Count == 0)
        {
            return;
        }

        var generated = new List<(int Cx, int Cz)>();
        foreach (var col in planned)
        {
            if (_generator.EnsureColumn(_sapi, col.DimId, col.Cx, col.Cz))
            {
                generated.Add((col.Cx, col.Cz));
            }

            foreach (var uid in col.PlayerUids)
            {
                if (byUid.TryGetValue(uid, out var player))
                {
                    _sapi.WorldManager.ForceSendChunkColumn(player, col.Cx, col.Cz, col.DimId);
                }
            }
        }

        if (generated.Count > 0)
        {
            DimensionGenerator.RelightColumns(_sapi, generated);
        }
    }

    /// <summary>Builds the planner input from online players currently in streaming dimensions.</summary>
    private List<StreamingPlayer> GatherStreamingPlayers(out Dictionary<string, IServerPlayer> byUid)
    {
        var result = new List<StreamingPlayer>();
        byUid = new Dictionary<string, IServerPlayer>();

        foreach (var player in _sapi.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer sp || sp.Entity?.Pos is not { } pos)
            {
                continue;
            }

            int dimId = pos.Dimension;
            var dim = _registry.GetByInternalId(dimId);
            if (dim?.StreamingLoadRadius is not { } radius)
            {
                continue;
            }

            int cx = (int)pos.X / 32;
            int cz = (int)pos.Z / 32;
            result.Add(new StreamingPlayer(sp.PlayerUID, dimId, cx, cz, radius));
            byUid[sp.PlayerUID] = sp;
        }

        return result;
    }

    /// <summary>In-memory loaded check. A dimension's columns sit at chunk-Y band dim*1024.</summary>
    private bool IsColumnLoaded(int dimId, int cx, int cz) =>
        _sapi.WorldManager.GetChunk(cx, dimId * 1024, cz) != null;
}
