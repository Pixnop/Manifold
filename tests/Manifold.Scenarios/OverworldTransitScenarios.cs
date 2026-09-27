namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Transit that starts and ends in dimension 0. This is the one transit family a stage 1 Atlas
/// rollback can cover (no mini-dimension chunk is ever loaded and no player joins), so both
/// scenarios adopt RollbackWorld. They deliberately share coordinates: each one asserts those
/// positions are air on entry, which turns the rollback contract itself into a tested property
/// instead of trusted infrastructure. Under shared-world isolation one of the two orderings
/// would fail. Since this class treats the rollback as a contract, both scenarios also set
/// StrictIsolation (Atlas 0.7.0): a degrade would still hand them a clean world via the full
/// recycle, but it would mean the contract silently stopped being exercised, so it fails.
/// </summary>
[Trait("Category", "E2E")]
public class OverworldTransitScenarios : ManifoldScenarioBase
{
    // Rollback-eligible: dimension-0 block writes only, no joined players.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Chest_Should_KeepContents_When_TeleportedWithinOverworld()
    {
        BlockPos source = World.Spawn.Offset(2, 1, 3);
        BlockPos target = World.Spawn.Offset(-3, 1, -2);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());

        await PlaceChest(source, "stick", 9);

        CommandResult result = await Ok(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 overworld {target.X} {target.Y} {target.Z}");
        Assert.Equal("moved", result.Message);

        await BlockBecomes(target, "game:chest-east");
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());

        AssertChestHolds(target, "stick", 9);
    }

    // Rollback-eligible: reads plus one no-op transit, no joined players. Runs against the same
    // coordinates as the scenario above on purpose; see the class summary.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task TeleportBlock_Should_ReportNoOp_When_SourceIsAir()
    {
        BlockPos source = World.Spawn.Offset(-3, 1, -2);
        BlockPos target = World.Spawn.Offset(2, 1, 3);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());

        CommandResult result = await Ok(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 overworld {target.X} {target.Y} {target.Z}");

        Assert.Equal("no-op", result.Message);
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());
    }
}
