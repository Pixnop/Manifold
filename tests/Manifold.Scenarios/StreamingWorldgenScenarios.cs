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

        CommandResult toStream = await World.ExecuteCommand("/atlasfx teleport-player atlasstreamer stream");
        Assert.True(toStream.Ok, toStream.Message);
        await World.Until(() => player.Position.dimension == streamId, timeoutTicks: 600);

        // The transit itself only ensures the landing region; the slab under the player must be
        // there once the streaming driver has had its ticks (budgeted at one column per tick).
        var underPlayer = new BlockPos(512, 3, 512, streamId);
        await World.Until(
            () => World.BlockAt(underPlayer).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 2400);

        // Move the player well outside the transit-ensured region; only the streaming driver can
        // generate there. Streaming's promise is no invisible walls at a region edge.
        var farLanding = new BlockPos(512 + 192, 8, 512, streamId);
        await player.TeleportTo(farLanding);
        var underFarLanding = new BlockPos(512 + 192, 3, 512, streamId);
        await World.Until(
            () => World.BlockAt(underFarLanding).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 2400);
    }
}
