using System;
using System.Collections.Generic;
using Manifold.Api.Worldgen;
using Vintagestory.API.MathTools;

namespace ManifoldSample;

/// <summary>
/// A worldgen strategy that fills every column with solid rock up to <see cref="RockHeight"/>,
/// sprinkling in ore blocks, and carves a lit air room around the fixed spawn point so a player
/// never lands inside rock. The ore pattern is salted (see <see cref="MiningWorldgenStrategy(int)"/>)
/// so a fresh instance produces a different layout without changing the spawn room.
/// </summary>
public sealed class MiningWorldgenStrategy : IWorldgenStrategy
{
    /// <summary>Height (inclusive) up to which every column is filled with rock.</summary>
    public const int RockHeight = 48;

    /// <summary>World X of the carved spawn room and the dimension's fixed spawn point.</summary>
    public const int SpawnX = 16;

    /// <summary>World Z of the carved spawn room and the dimension's fixed spawn point.</summary>
    public const int SpawnZ = 16;

    /// <summary>
    /// World Y of the dimension's fixed spawn point: one block above the solid rock floor,
    /// i.e. the same height as the room's floor air layer.
    /// </summary>
    public const int SpawnY = RoomFloorY;

    private const int RoomRadius = 2;

    // The room's air layers run RoomFloorY..RoomCeilingY inclusive, so the solid rock floor
    // right below the room sits at RoomFloorY - 1.
    private const int RoomFloorY = 22;
    private const int RoomCeilingY = 26;
    private const int OreChanceDenominator = 40;

    private static readonly string[] OreCodes =
    {
        "game:ore-medium-nativecopper-granite",
        "game:ore-medium-hematite-granite",
        "game:ore-quartz-granite",
    };

    private readonly int _salt;
    private readonly List<int> _oreBlockIds = new();
    private int _rockBlockId;
    private int _lightBlockId;

    /// <summary>Creates the strategy with the given ore salt.</summary>
    /// <param name="salt">
    /// Mixed into the per-column RNG seed so a new instance (e.g. after a reset) generates a
    /// different ore layout even though the rock shell and spawn room stay identical.
    /// </param>
    public MiningWorldgenStrategy(int salt)
    {
        _salt = salt;
    }

    /// <inheritdoc/>
    public void OnInitialize(IWorldgenInitContext ctx)
    {
        _rockBlockId = WorldgenHelpers.ResolveFirst(ctx.Api, "game:rock-granite", "game:rock-andesite", "game:rock-basalt");

        // game:torch-basic-lit-up burns out to torch-basic-burnedout-up after 48 in-game hours
        // (see its transientPropsByType). Fine for a sample room players only pass through
        // occasionally; a dimension meant to stay lit unattended would want a non-transient
        // light block instead.
        _lightBlockId = WorldgenHelpers.ResolveFirst(ctx.Api, "game:torch-basic-lit-up");

        _oreBlockIds.Clear();
        foreach (var code in OreCodes)
        {
            int id = WorldgenHelpers.ResolveFirst(ctx.Api, code);
            if (id != 0)
            {
                _oreBlockIds.Add(id);
            }
        }

        if (_rockBlockId == 0)
        {
            ctx.Api.Logger.Warning(
                "[ManifoldSample] MiningWorldgenStrategy: no rock block resolved; mining dim will be void.");
        }

        if (_lightBlockId == 0)
        {
            ctx.Api.Logger.Warning(
                "[ManifoldSample] MiningWorldgenStrategy: no light block resolved; spawn room will be unlit.");
        }

        if (_oreBlockIds.Count == 0)
        {
            ctx.Api.Logger.Warning(
                "[ManifoldSample] MiningWorldgenStrategy: no ore block resolved; mining dim will be plain rock.");
        }
    }

    /// <inheritdoc/>
    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        if (_rockBlockId == 0)
        {
            return;
        }

        int baseX = ctx.ChunkX * WorldgenHelpers.ChunkSize;
        int baseZ = ctx.ChunkZ * WorldgenHelpers.ChunkSize;

        // Salted so a freshly-registered instance (after /miningreset) draws a different ore
        // sequence for the same chunk, while the rock shell and spawn room stay deterministic.
        ctx.Rng.InitPositionSeed(baseX + (_salt * 104729), baseZ);

        for (int lx = 0; lx < WorldgenHelpers.ChunkSize; lx++)
        {
            for (int lz = 0; lz < WorldgenHelpers.ChunkSize; lz++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;
                bool inRoomColumn = Math.Abs(wx - SpawnX) <= RoomRadius && Math.Abs(wz - SpawnZ) <= RoomRadius;

                for (int y = 1; y <= RockHeight; y++)
                {
                    if (inRoomColumn && y >= RoomFloorY && y <= RoomCeilingY)
                    {
                        // Leave the room air; drop the single light block on its floor centre.
                        if (_lightBlockId != 0 && wx == SpawnX && wz == SpawnZ && y == RoomFloorY)
                        {
                            ctx.BlockAccessor.SetBlock(_lightBlockId, new BlockPos(wx, y, wz, ctx.DimensionId));
                        }

                        continue;
                    }

                    int blockId = _rockBlockId;
                    if (_oreBlockIds.Count > 0 && ctx.Rng.NextInt(OreChanceDenominator) == 0)
                    {
                        blockId = _oreBlockIds[ctx.Rng.NextInt(_oreBlockIds.Count)];
                    }

                    ctx.BlockAccessor.SetBlock(blockId, new BlockPos(wx, y, wz, ctx.DimensionId));
                }
            }
        }
    }
}
