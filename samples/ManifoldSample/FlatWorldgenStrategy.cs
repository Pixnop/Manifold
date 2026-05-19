using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ManifoldSample;

/// <summary>
/// A worldgen strategy that fills the bottom of every chunk column with a solid floor,
/// producing a flat test world with clear visual reference points (so movement is observable).
/// </summary>
public sealed class FlatWorldgenStrategy : IWorldgenStrategy
{
    private const int FloorHeight = 4;

    private int _floorBlockId;
    private int _topBlockId;

    /// <inheritdoc/>
    public void OnInitialize(IWorldgenInitContext ctx)
    {
        _floorBlockId = ResolveFirst(ctx.Api, "game:rock-granite", "game:rock-andesite", "game:rock-basalt");
        _topBlockId = ResolveFirst(ctx.Api, "game:soil-medium-normal", "game:soil-low-normal", "game:rock-granite");

        if (_floorBlockId == 0)
        {
            ctx.Api.Logger.Warning(
                "[ManifoldSample] FlatWorldgenStrategy: no floor block resolved; flat dim will be void.");
        }
        else
        {
            ctx.Api.Logger.Notification(
                "[ManifoldSample] FlatWorldgenStrategy ready: floor id={0}, top id={1}, height={2}.",
                _floorBlockId,
                _topBlockId,
                FloorHeight);
        }
    }

    /// <inheritdoc/>
    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        if (_floorBlockId == 0)
        {
            return;
        }

        const int chunkSize = 32;

        int baseX = ctx.ChunkX * chunkSize;
        int baseZ = ctx.ChunkZ * chunkSize;

        for (int lx = 0; lx < chunkSize; lx++)
        {
            for (int lz = 0; lz < chunkSize; lz++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;
                for (int y = 1; y <= FloorHeight; y++)
                {
                    int blockId = (y == FloorHeight && _topBlockId != 0) ? _topBlockId : _floorBlockId;
                    var pos = new BlockPos(wx, y, wz, ctx.DimensionId);
                    ctx.BlockAccessor.SetBlock(blockId, pos);
                }
            }
        }
    }

    private static int ResolveFirst(ICoreServerAPI api, params string[] codes)
    {
        foreach (var code in codes)
        {
            var block = api.World.GetBlock(new AssetLocation(code));
            if (block is not null && block.Id != 0)
            {
                return block.Id;
            }
        }

        return 0;
    }
}
