namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

/// <summary>
/// Streaming worldgen against the real engine: nothing pregenerates the stream dimension, the
/// streaming driver alone must produce terrain around a player as they arrive and as they move.
/// This is the Manifold surface mocks are most blind to (scheduler budget, chunk load callbacks,
/// view-distance extension). The dimension is registered with WithStreamingBudget(1), so every
/// column generated here also went through the per-dimension budget gate.
/// </summary>
// rollback-stage2-candidate: needs a joined player (streaming only follows players), which
// hard-refuses stage 1 rollback with an AtlasSetupException; the generated columns also live in
// a mini-dimension, making it a stage 3 candidate on top. FreshWorld is the only isolation
// available if a future streaming scenario needs a clean slate.
[Trait("Category", "E2E")]
public class StreamingWorldgenScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task StreamingDimension_Should_GenerateTerrain_When_PlayerArrivesAndMoves()
    {
        int streamId = await DimensionId("stream");
        ITestPlayer player = await World.JoinPlayer("atlasstreamer");
        await World.Ticks(2);

        await Ok("/atlasfx teleport-player atlasstreamer stream");
        await World.Until(() => player.Position.dimension == streamId, timeoutTicks: 600);

        // The transit itself only ensures the landing region; the slab under the player must be
        // there once the streaming driver has had its ticks (budgeted at one column per tick).
        var underPlayer = new BlockPos(512, 3, 512, streamId);
        await BlockBecomes(underPlayer, "game:rock-granite", timeoutTicks: 2400);

        // Move the player well outside the transit-ensured region; only the streaming driver can
        // generate there. Streaming's promise is no invisible walls at a region edge.
        var farLanding = new BlockPos(512 + 192, 8, 512, streamId);
        await player.TeleportTo(farLanding);
        var underFarLanding = new BlockPos(512 + 192, 3, 512, streamId);
        await BlockBecomes(underFarLanding, "game:rock-granite", timeoutTicks: 2400);
    }

    // Issue #136: the engine never unloads the chunks of a custom dimension, so a destroyed
    // dimension's columns stay in memory under its engine id. The streaming driver used to skip
    // every column already in memory: a new streaming dimension on the recycled id kept the dead
    // one's terrain everywhere outside its landing pad. The marker sits three columns out, inside
    // the streaming window and outside the pad (generation radius 1).
    [AtlasScenario(TimeoutMs = 180000)]
    public async Task StreamingDimension_Should_RegenerateLeftoverColumns_When_ReusingARecycledEngineId()
    {
        await Ok("/atlasfx2 create-streaming recyclestreama");
        int idA = await DimensionId("recyclestreama");
        ITestPlayer player = await World.JoinPlayer("atlasrecycler");
        await Ok("/atlasfx teleport-player atlasrecycler recyclestreama");
        await World.Until(() => player.Position.dimension == idA, timeoutTicks: 600);

        var slabInA = new BlockPos(512 + 96, 3, 512, idA);
        await BlockBecomes(slabInA, "game:rock-granite", timeoutTicks: 2400);
        var markerInA = new BlockPos(512 + 96, 5, 512, idA);
        World.SetBlock("game:rock-andesite", markerInA);
        await World.Ticks(2);
        Assert.Equal("game:rock-andesite", World.BlockAt(markerInA).Code?.ToString());

        // The last occupant leaving reaps the ephemeral dimension and frees its engine id.
        await Ok("/atlasfx teleport-player atlasrecycler overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Equal("unregistered", (await World.ExecuteCommand("/atlasfx state recyclestreama")).Message);

        await Ok("/atlasfx2 create-streaming recyclestreamb");
        int idB = await DimensionId("recyclestreamb");
        Assert.Equal(idA, idB);
        await Ok("/atlasfx teleport-player atlasrecycler recyclestreamb");
        await World.Until(() => player.Position.dimension == idB, timeoutTicks: 600);

        await BlockBecomes(new BlockPos(512 + 96, 5, 512, idB), "game:air", timeoutTicks: 2400);
        Assert.Equal("game:rock-granite", World.BlockAt(new BlockPos(512 + 96, 3, 512, idB)).Code?.ToString());

        await Ok("/atlasfx teleport-player atlasrecycler overworld");
    }

    // The other side of the same check: a column Manifold generated for a dimension that still
    // exists is never generated again, so what a player built there is still there when they
    // come back. The wait after the return is long enough for the streaming driver to have gone
    // over the window out to the marker's column several times (budget of one column per tick).
    [AtlasScenario(TimeoutMs = 180000)]
    public async Task StreamingDimension_Should_KeepPlayerBlocks_When_PlayerLeavesAndComesBack()
    {
        int streamId = await DimensionId("stream");
        ITestPlayer player = await World.JoinPlayer("atlasreturner");
        await Ok("/atlasfx teleport-player atlasreturner stream");
        await World.Until(() => player.Position.dimension == streamId, timeoutTicks: 600);

        var slab = new BlockPos(512, 3, 512 + 96, streamId);
        await BlockBecomes(slab, "game:rock-granite", timeoutTicks: 2400);
        var marker = new BlockPos(512, 5, 512 + 96, streamId);
        World.SetBlock("game:rock-andesite", marker);
        await World.Ticks(2);

        await Ok("/atlasfx teleport-player atlasreturner overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        await World.Ticks(100);

        await Ok("/atlasfx teleport-player atlasreturner stream");
        await World.Until(() => player.Position.dimension == streamId, timeoutTicks: 600);
        await World.Ticks(900);

        Assert.Equal("game:rock-andesite", World.BlockAt(marker).Code?.ToString());
        Assert.Equal("game:rock-granite", World.BlockAt(slab).Code?.ToString());

        // The stream dimension is persistent and shared by the class: leave it as it was found.
        World.SetBlock("game:air", marker);
        await Ok("/atlasfx teleport-player atlasreturner overworld");
    }

    // The streaming window follows the radius the engine sends to each player (their view distance
    // in chunks, rounded up, capped by the server's), plus one chunk of lead, not the server's
    // maximum: a player with a view distance of 128 blocks (4 chunks) holds 11 x 11 columns where
    // the server radius of 12 used to give 25 x 25. The engine never unloads a custom dimension's
    // columns, so every check counts inside a box around a position no other scenario visits.
    [AtlasScenario(TimeoutMs = 300000)]
    public async Task StreamingDimension_Should_KeepOnlyThePlayersViewWindow_When_ViewDistanceIsBelowTheServerRadius()
    {
        int dimId = await CreateStreamingDimension("viewwindow");
        ITestPlayer player = await EnterDimension("atlasviewer", "viewwindow", dimId, viewDistance: 128);
        (int cx, int cz) = await GoTo(player, dimId, 4096);

        await World.Until(() => MissingColumns(dimId, cx, cz, 5) == 0, timeoutTicks: 2400);
        await World.Ticks(200);

        Assert.Equal(121, LoadedColumnsAround(dimId, cx, cz));
    }

    // The window is read from the player on every driver tick, so a change of view distance moves
    // it: a larger one widens it, a smaller one stops generating further out (what is already
    // loaded stays, the engine never unloads it).
    [AtlasScenario(TimeoutMs = 600000)]
    public async Task StreamingDimension_Should_FollowAViewDistanceChange_When_ThePlayerChangesItMidSession()
    {
        int dimId = await CreateStreamingDimension("viewchange");
        ITestPlayer player = await EnterDimension("atlaschanger", "viewchange", dimId, viewDistance: 128);
        (int cx, int cz) = await GoTo(player, dimId, 8192);
        await World.Until(() => MissingColumns(dimId, cx, cz, 5) == 0, timeoutTicks: 2400);

        // 256 blocks are 8 chunks, plus the lead: 19 x 19.
        SetViewDistance(player, 256);
        await World.Until(() => MissingColumns(dimId, cx, cz, 9) == 0, timeoutTicks: 4800);
        await World.Ticks(200);
        Assert.Equal(361, LoadedColumnsAround(dimId, cx, cz));

        // 32 blocks are below the dimension's loadRadius of 2, which is the floor: 5 x 5, somewhere new.
        SetViewDistance(player, 32);
        (int cx2, int cz2) = await GoTo(player, dimId, 12288);
        await World.Until(() => MissingColumns(dimId, cx2, cz2, 2) == 0, timeoutTicks: 2400);
        await World.Ticks(200);
        Assert.Equal(25, LoadedColumnsAround(dimId, cx2, cz2));
    }

    // Each player's window is sized on their own view distance, even when they share a dimension.
    [AtlasScenario(TimeoutMs = 600000)]
    public async Task StreamingDimension_Should_SizeEachWindow_When_PlayersWithDifferentViewDistancesShareIt()
    {
        int dimId = await CreateStreamingDimension("viewshared");
        ITestPlayer near = await EnterDimension("atlasnear", "viewshared", dimId, viewDistance: 64);
        ITestPlayer far = await EnterDimension("atlasfar", "viewshared", dimId, viewDistance: 192);

        (int nearX, int nearZ) = await GoTo(near, dimId, 16384);
        (int farX, int farZ) = await GoTo(far, dimId, 20480);
        await World.Until(
            () => MissingColumns(dimId, nearX, nearZ, 3) == 0 && MissingColumns(dimId, farX, farZ, 7) == 0,
            timeoutTicks: 4800);
        await World.Ticks(200);

        // 64 blocks are 2 chunks, plus the lead: 7 x 7. 192 blocks are 6 chunks, plus the lead: 15 x 15.
        Assert.Equal(49, LoadedColumnsAround(dimId, nearX, nearZ));
        Assert.Equal(225, LoadedColumnsAround(dimId, farX, farZ));
    }

    /// <summary>
    /// Sets the view distance the engine reads for a player (blocks). <c>LastApprovedViewDistance</c>
    /// has no usable setter (it calls itself), so this writes the field it reads, as the engine does when
    /// the client asks for a new view distance. Test plumbing: the code under test never does this.
    /// </summary>
    private static void SetViewDistance(ITestPlayer player, int blocks)
    {
        object worldData = player.Player.WorldData;
        worldData.GetType().GetField("Viewdistance")!.SetValue(worldData, blocks);
    }

    private async Task<int> CreateStreamingDimension(string path)
    {
        await Ok("/atlasfx2 create-streaming " + path);
        return await DimensionId(path);
    }

    private async Task<ITestPlayer> EnterDimension(string playerName, string path, int dimId, int viewDistance)
    {
        ITestPlayer player = await World.JoinPlayer(playerName);
        SetViewDistance(player, viewDistance);
        await Ok($"/atlasfx teleport-player {playerName} {path}");
        await World.Until(() => player.Position.dimension == dimId, timeoutTicks: 600);
        return player;
    }

    /// <summary>Teleports the player to the middle of the chunk column at (<paramref name="blockX"/>, 4096) and returns its chunk coordinates.</summary>
    private async Task<(int Cx, int Cz)> GoTo(ITestPlayer player, int dimId, int blockX)
    {
        const int blockZ = 4096;
        await player.TeleportTo(new BlockPos(blockX + 16, 8, blockZ + 16, dimId));
        return (blockX / 32, blockZ / 32);
    }

    /// <summary>The chunk columns of the dimension that are loaded on the server (any slice of the column).</summary>
    private HashSet<(int Cx, int Cz)> LoadedColumns(int dimId)
    {
        IWorldManagerAPI worldManager = World.Api.WorldManager;
        long strideX = worldManager.ChunkIndex3D(0, 0, 1);
        long strideZ = worldManager.ChunkIndex3D(0, 1, 0) / strideX;
        var columns = new HashSet<(int Cx, int Cz)>();
        foreach (long key in worldManager.AllLoadedChunks.Keys)
        {
            long rest = key / strideX;
            if ((int)((rest / strideZ) / 1024) == dimId)
            {
                columns.Add(((int)(key % strideX), (int)(rest % strideZ)));
            }
        }

        return columns;
    }

    /// <summary>How many columns of the square of the given radius around a chunk are not loaded yet.</summary>
    private int MissingColumns(int dimId, int cx, int cz, int radius)
    {
        HashSet<(int Cx, int Cz)> loaded = LoadedColumns(dimId);
        int missing = 0;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                missing += loaded.Contains((cx + dx, cz + dz)) ? 0 : 1;
            }
        }

        return missing;
    }

    /// <summary>
    /// How many columns are loaded in a box around a chunk that is wider than any window these
    /// scenarios ask for (the server's own radius, 12 here, is the widest a window ever was).
    /// </summary>
    private int LoadedColumnsAround(int dimId, int cx, int cz) =>
        LoadedColumns(dimId).Count(c => Math.Abs(c.Cx - cx) <= 14 && Math.Abs(c.Cz - cz) <= 14);
}
