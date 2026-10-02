namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
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
}
