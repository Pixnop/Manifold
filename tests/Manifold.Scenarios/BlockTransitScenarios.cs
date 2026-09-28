namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

// Strict rollback (see README): nothing here joins players.
[Trait("Category", "E2E")]
public class BlockTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Chest_Should_KeepContents_When_TeleportedAcrossDimensions()
    {
        int flatId = await DimensionId("flat");

        BlockPos source = World.Spawn.Offset(3, 1, 0);
        await PlaceChest(source, "stick", 5);

        CommandResult result = await World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 520 6 520");

        var target = new BlockPos(520, 6, 520, flatId);
        await BlockBecomes(target, "game:chest-east");

        Assert.True(result.Ok, "TeleportBlock reported failure.");
        Assert.Equal("moved", result.Message);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());

        AssertChestHolds(target, "stick", 5);
    }

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Chest_Should_KeepContents_When_TeleportedBackToOverworld()
    {
        int flatId = await DimensionId("flat");

        BlockPos source = World.Spawn.Offset(5, 1, 0);
        await PlaceChest(source, "flint", 7);

        await Ok(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 524 6 524");

        var flatPos = new BlockPos(524, 6, 524, flatId);
        await BlockBecomes(flatPos, "game:chest-east");

        // Return leg: source is in the mini-dimension, destination back in dimension 0.
        BlockPos home = World.Spawn.Offset(7, 1, 0);
        CommandResult inbound = await Ok(
            $"/atlasfx teleport-block 524 6 524 {flatId} overworld {home.X} {home.Y} {home.Z}");
        Assert.Equal("moved", inbound.Message);

        await BlockBecomes(home, "game:chest-east");
        Assert.Equal("game:air", World.BlockAt(flatPos).Code.ToString());

        AssertChestHolds(home, "flint", 7);
    }

    // Rollback-eligible: dimension-0 block writes only, no joined players.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task TeleportBlock_Should_Refuse_And_Leave_A_Bed_Intact()
    {
        // A bed is two independently placed blocks (head/feet) linked by code and facing, not by the
        // engine's multiblock mechanism: the one vanilla case MultiPositionBlockDetector matches by
        // block class name instead. North's normal is (0, 0, -1); head sits one block north of feet.
        BlockPos feet = World.Spawn.Offset(9, 1, 0);
        BlockPos head = feet.AddCopy(0, 0, -1);
        BlockPos target = World.Spawn.Offset(-9, 1, 0);
        World.SetBlock("game:bed-wood-feet-north", feet);
        World.SetBlock("game:bed-wood-head-north", head);
        await World.Ticks(2);

        CommandResult result = await Ok(
            $"/atlasfx teleport-block {head.X} {head.Y} {head.Z} 0 overworld {target.X} {target.Y} {target.Z}");

        Assert.Equal("refused", result.Message);
        Assert.Equal("game:bed-wood-head-north", World.BlockAt(head).Code.ToString());
        Assert.Equal("game:bed-wood-feet-north", World.BlockAt(feet).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());
    }

    // Rollback-eligible: dimension-0 block writes only, no joined players.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task TeleportBlock_Should_Refuse_And_Leave_A_Large_Trough_Half_Intact()
    {
        // Like the bed, the large trough links its head/feet halves purely by block class: matched
        // by runtime type name, not by the engine's multiblock mechanism, so a single half is enough
        // to exercise the check.
        BlockPos head = World.Spawn.Offset(9, 1, 3);
        BlockPos target = World.Spawn.Offset(-9, 1, -3);
        World.SetBlock("game:trough-genericwood-large-head-north", head);
        await World.Ticks(2);

        CommandResult result = await Ok(
            $"/atlasfx teleport-block {head.X} {head.Y} {head.Z} 0 overworld {target.X} {target.Y} {target.Z}");

        Assert.Equal("refused", result.Message);
        Assert.Equal("game:trough-genericwood-large-head-north", World.BlockAt(head).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());
    }

    // Rollback-eligible: dimension-0 block writes only, no joined players.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task TeleportBlock_Should_Refuse_And_Leave_A_Legacy_Door_Half_Intact()
    {
        // The legacy door (pre-BlockBehaviorDoor worlds) links its up/down halves purely by block
        // class, the same way the bed does: matched by runtime type name, not the engine's
        // multiblock mechanism, so a single half is enough to exercise the check.
        BlockPos down = World.Spawn.Offset(9, 1, -3);
        BlockPos target = World.Spawn.Offset(-9, 1, 3);
        World.SetBlock("game:door-plank-north-down-closed-left", down);
        await World.Ticks(2);

        CommandResult result = await Ok(
            $"/atlasfx teleport-block {down.X} {down.Y} {down.Z} 0 overworld {target.X} {target.Y} {target.Z}");

        Assert.Equal("refused", result.Message);
        Assert.Equal("game:door-plank-north-down-closed-left", World.BlockAt(down).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());
    }

    // Rollback-eligible: dimension-0 block writes only, no joined players.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task TeleportBlock_Should_Refuse_And_Leave_A_Door_Intact()
    {
        // A door's main cell is the engine's BlockGeneric+BlockBehaviorDoor controller; height 2
        // (the vanilla default) fills the cell above it with the engine's BlockMultiblock satellite
        // (game:multiblock-monolithic-0-p1-0), the same placeholder TryPlaceBlock would use. Moving
        // the controller must be refused by the neighbour-search side of the detector.
        BlockPos main = World.Spawn.Offset(-9, 1, 3);
        BlockPos filler = main.AddCopy(0, 1, 0);
        BlockPos target = World.Spawn.Offset(9, 1, -3);
        World.SetBlock("game:door-solid-oak", main);
        World.SetBlock("game:multiblock-monolithic-0-p1-0", filler);
        await World.Ticks(2);

        CommandResult result = await Ok(
            $"/atlasfx teleport-block {main.X} {main.Y} {main.Z} 0 overworld {target.X} {target.Y} {target.Z}");

        Assert.Equal("refused", result.Message);
        Assert.Equal("game:door-solid-oak", World.BlockAt(main).Code.ToString());
        Assert.Equal("game:multiblock-monolithic-0-p1-0", World.BlockAt(filler).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());
    }
}
