using System;
using System.Collections.Generic;
using Manifold.Internal.Util;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary><see cref="IRelightEngine"/> over the public game API.</summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class EngineRelight : IRelightEngine
{
    private const int BlocksPerChunk = ChunkMath.ChunkSize * ChunkMath.ChunkSize * ChunkMath.ChunkSize;

    private readonly ICoreServerAPI _sapi;
    private IBlockAccessor? _relightAccessor;
    private bool[]? _emitsLight;

    /// <summary>Initializes a new instance of the <see cref="EngineRelight"/> class.</summary>
    /// <param name="sapi">Server API.</param>
    public EngineRelight(ICoreServerAPI sapi)
    {
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
    }

    // synchronize:false: an exchange packet would make each client recompute the block light on
    // its side, but clients need the chunks resent anyway for the sunlight FullRelight changed, and
    // the resend carries the block light the server computed.
    private IBlockAccessor RelightAccessor =>
        _relightAccessor ??= _sapi.World.GetBlockAccessor(synchronize: false, relight: true, strict: false);

    /// <inheritdoc/>
    public bool FullRelight(BlockPos min, BlockPos max)
    {
        try
        {
            // Never the resending overload: it resends by plain chunk Y, which is the overworld's
            // chunks, not this dimension's. Broadcast does the resend with the dimension's own.
            _sapi.WorldManager.FullRelight(min, max, false);

            // The engine's resend used to mark the relit chunks dirty (with the overworld's chunk Y);
            // without it nothing does, and the recomputed sunlight would not be saved.
            ForEachChunk(min, max, ChunkMath.ChunkSize, (cx, cy, cz) => ChunkAt(cx, cy, cz, min.dimension)?.MarkModified());
            return true;
        }
        catch (Exception ex)
        {
            _sapi.Logger.Warning("[Manifold] Relight of dim {0} {1}..{2} failed: {3}", min.dimension, min, max, ex);
            return false;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<BlockPos> FindLightSources(BlockPos min, BlockPos max)
    {
        var found = new List<BlockPos>();

        // FullRelight clears one chunk around the box; a source up to one more chunk away shines
        // into what it cleared.
        ForEachChunk(min, max, 2 * ChunkMath.ChunkSize, (cx, cy, cz) => CollectSources(cx, cy, cz, min.dimension, found));
        return found;
    }

    /// <inheritdoc/>
    public bool IsGateOpen(int chunkX, int chunkZ) => _sapi.WorldManager.GetMapChunk(chunkX, chunkZ) != null;

    /// <inheritdoc/>
    public bool QueueBlockLight(BlockPos pos)
    {
        Block? block = EmittingBlockAt(pos);
        if (block is null)
        {
            return false;
        }

        // Exchanging a block for itself leaves the world as it is (no placed/removed handler, the
        // block entity is kept and only gets its OnExchanged call, with the same block) but makes
        // the engine queue its block-light update for the position.
        RelightAccessor.ExchangeBlock(block.BlockId, pos);
        return true;
    }

    /// <inheritdoc/>
    public bool EmitsLight(BlockPos pos) => EmittingBlockAt(pos) is not null;

    /// <inheritdoc/>
    public bool IsLit(BlockPos pos) =>
        _sapi.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight) > 0;

    /// <inheritdoc/>
    public void CollectAffectedChunks(BlockPos min, BlockPos max, ISet<(int Cx, int Cy, int Cz)> chunks)
    {
        ForEachChunk(min, max, ChunkMath.ChunkSize, (cx, cy, cz) =>
        {
            if (ChunkAt(cx, cy, cz, min.dimension) is not null)
            {
                chunks.Add((cx, cy, cz));
            }
        });
    }

    /// <inheritdoc/>
    public void Resend(int dimId, IReadOnlyCollection<(int Cx, int Cy, int Cz)> chunks)
    {
        // Not BroadcastChunk: it only tests horizontal range, so a player at the same X/Z in another
        // dimension would be sent this dimension's chunks.
        var players = new List<IServerPlayer>(OccupancyScan.PlayersIn(_sapi, dimId));
        if (players.Count == 0)
        {
            return;
        }

        int stride = dimId * ChunkMath.DimensionChunkYStride;
        foreach (var (cx, cy, cz) in chunks)
        {
            // A chunk unloaded since it was listed must not be queued: the engine would keep the
            // request for it forever.
            if (ChunkAt(cx, cy, cz, dimId) is null)
            {
                continue;
            }

            foreach (var player in players)
            {
                _sapi.WorldManager.SendChunk(cx, cy + stride, cz, player, true);
            }
        }
    }

    /// <inheritdoc/>
    public void Warn(string message) => _sapi.Logger.Warning(message);

    private static int Low(int a, int b, int margin) => Math.Max(Math.Min(a, b) - margin, 0) / ChunkMath.ChunkSize;

    private static int High(int a, int b, int margin, int mapSize) =>
        Math.Min(Math.Max(a, b) + margin, mapSize - 1) / ChunkMath.ChunkSize;

    private static BlockPos PosOf(int cx, int cy, int cz, int index, int dimId) => new(
        (cx * ChunkMath.ChunkSize) + (index % ChunkMath.ChunkSize),
        (cy * ChunkMath.ChunkSize) + (index / (ChunkMath.ChunkSize * ChunkMath.ChunkSize)),
        (cz * ChunkMath.ChunkSize) + ((index / ChunkMath.ChunkSize) % ChunkMath.ChunkSize),
        dimId);

    /// <summary>Visits every chunk position within <paramref name="margin"/> blocks of the box, clamped to the map.</summary>
    private void ForEachChunk(BlockPos min, BlockPos max, int margin, Action<int, int, int> visit)
    {
        var world = _sapi.WorldManager;
        int maxCx = High(min.X, max.X, margin, world.MapSizeX);
        int maxCy = High(min.Y, max.Y, margin, world.MapSizeY);
        int maxCz = High(min.Z, max.Z, margin, world.MapSizeZ);
        for (int cx = Low(min.X, max.X, margin); cx <= maxCx; cx++)
        {
            for (int cz = Low(min.Z, max.Z, margin); cz <= maxCz; cz++)
            {
                for (int cy = Low(min.Y, max.Y, margin); cy <= maxCy; cy++)
                {
                    visit(cx, cy, cz);
                }
            }
        }
    }

    /// <summary>
    /// Adds the chunk's possible light sources: the positions the engine already tracks as lights,
    /// and every block whose type emits light (a block written without relight, by worldgen or a
    /// paste, is not tracked by the engine yet).
    /// </summary>
    private void CollectSources(int cx, int cy, int cz, int dimId, List<BlockPos> found)
    {
        IWorldChunk? chunk = ChunkAt(cx, cy, cz, dimId);
        if (chunk is null || chunk.Empty)
        {
            return;
        }

        try
        {
            chunk.Unpack();
            HashSet<int> indices = TrackedLights(chunk);
            bool[] emits = EmittingBlockIds();

            // Most chunks hold no light-emitting solid block: the chunk's palette says so without
            // reading its 32768 cells. The fluid layer has no palette to ask, but reads are cheap.
            var palette = new List<int>();
            chunk.Data.FuzzyListBlockIds(palette);
            bool solidEmitter = palette.Exists(id => id >= 0 && id < emits.Length && emits[id]);
            for (int index = 0; index < BlocksPerChunk; index++)
            {
                if ((solidEmitter && emits[chunk.Data.GetBlockId(index, BlockLayersAccess.Solid)]) || emits[chunk.Data.GetFluid(index)])
                {
                    indices.Add(index);
                }
            }

            foreach (int index in indices)
            {
                found.Add(PosOf(cx, cy, cz, index, dimId));
            }
        }
        catch (Exception ex)
        {
            _sapi.Logger.Warning(
                "[Manifold] Relight of dim {0}: chunk ({1}, {2}, {3}) could not be scanned for light sources: {4}", dimId, cx, cy, cz, ex);
        }
    }

    /// <summary>
    /// Copies the positions the engine tracks as lights. The engine's relight thread adds to and
    /// removes from that set while it works, so the copy can fail; the block scan that follows
    /// finds every source whose block type emits light anyway.
    /// </summary>
    private static HashSet<int> TrackedLights(IWorldChunk chunk)
    {
        try
        {
            return new HashSet<int>(chunk.LightPositions ?? new HashSet<int>());
        }
        catch (InvalidOperationException)
        {
            return new HashSet<int>();
        }
    }

    private IWorldChunk? ChunkAt(int cx, int cy, int cz, int dimId) =>
        _sapi.WorldManager.GetChunk(cx, cy + (dimId * ChunkMath.DimensionChunkYStride), cz);

    private bool[] EmittingBlockIds()
    {
        if (_emitsLight is not null)
        {
            return _emitsLight;
        }

        var blocks = _sapi.World.Blocks;
        var emits = new bool[blocks.Count];
        for (int id = 0; id < emits.Length; id++)
        {
            emits[id] = blocks[id] is { } block && block.LightHsv[2] > 0;
        }

        _emitsLight = emits;
        return emits;
    }

    private Block? EmittingBlockAt(BlockPos pos)
    {
        var accessor = _sapi.World.BlockAccessor;
        foreach (int layer in new[] { BlockLayersAccess.Solid, BlockLayersAccess.Fluid })
        {
            Block? block = accessor.GetBlock(pos, layer);
            if (block is not null && block.GetLightHsv(accessor, pos)[2] > 0)
            {
                return block;
            }
        }

        return null;
    }
}
