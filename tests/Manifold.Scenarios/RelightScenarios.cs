namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// IManifoldServer.RelightRegion and /manifold relight against the real engine's lighting. Every
/// scenario puts a player in its own roofed dimension first: the engine only computes block light
/// for a column whose overworld map chunk (same X/Z) is loaded, and it loads that on its own under
/// a player, whatever dimension the player stands in. Nothing here force-loads an overworld column.
/// The lights sit in chunk (16, 16), away from chunk (0, 0, 0) where the engine's own FullRelight
/// happens to restore block light correctly.
/// </summary>
[Trait("Category", "E2E")]
public class RelightScenarios : ManifoldScenarioBase
{
    private const string Lantern = "game:paperlantern-on";

    // Wall-clock, not ticks: the restore runs on the engine's relight thread and Manifold's retry
    // listener, both of which keep real time.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    [AtlasScenario]
    public async Task RelightRegion_Should_KeepBlockLight_When_SourceWasLitByTheEngine()
    {
        int dimId = await PlayerInRoofedDimension("relighta", "relight_keep");
        var lantern = new BlockPos(520, 6, 520, dimId);
        var beside = new BlockPos(521, 6, 520, dimId);

        World.SetBlock(Lantern, lantern);
        await World.Until(() => BlockLight(beside) == 20, timeoutTicks: 600);

        await Ok("/atlasfx2 relight-region relight_keep 518 5 518 522 7 522");
        await Task.Delay(Settle);
        await World.Ticks(5);

        Assert.Equal(20, BlockLight(beside));
    }

    [AtlasScenario]
    public async Task ManifoldRelightCommand_Should_KeepBlockLight_When_SourceWasLitByTheEngine()
    {
        ITestPlayer player = await JoinSurvivalPlayer("relightb");
        await Ok("/player relightb role admin");
        int dimId = await EnterRoofedDimension(player, "relight_cmd");
        var lantern = new BlockPos(520, 6, 520, dimId);
        var beside = new BlockPos(521, 6, 520, dimId);

        World.SetBlock(Lantern, lantern);
        await World.Until(() => BlockLight(beside) == 20, timeoutTicks: 600);

        CommandResult relight = await player.ExecuteCommand("/manifold relight 0");
        Assert.True(relight.Ok, relight.Message);
        await Task.Delay(Settle);
        await World.Ticks(5);

        Assert.Equal(20, BlockLight(beside));
    }

    [AtlasScenario]
    public async Task RelightRegion_Should_KeepLightShiningIn_When_SourceIsOutsideTheClearedChunks()
    {
        int dimId = await PlayerInRoofedDimension("relighte", "relight_edge");

        // The box sits in chunk 15, so the engine clears chunks up to 16 (x 480..543). The lantern
        // is in chunk 17 and keeps its own light, but what it shone into chunk 16 is erased.
        var lantern = new BlockPos(546, 6, 520, dimId);
        var insideCleared = new BlockPos(543, 6, 520, dimId);
        World.SetBlock(Lantern, lantern);
        await World.Until(() => BlockLight(insideCleared) == 18, timeoutTicks: 600);

        await Ok("/atlasfx2 relight-region relight_edge 490 6 520 490 6 520");
        await Task.Delay(Settle);
        await World.Ticks(5);

        Assert.Equal(18, BlockLight(insideCleared));
    }

    [AtlasScenario]
    public async Task RelightRegion_Should_LightASource_When_ItWasPlacedWithoutRelight()
    {
        int dimId = await PlayerInRoofedDimension("relightc", "relight_raw");
        var lantern = new BlockPos(520, 6, 520, dimId);
        var beside = new BlockPos(521, 6, 520, dimId);

        // What a worldgen strategy, a ColumnGenerated subscriber or a schematic paste does: a write
        // the engine never lights.
        int lanternId = World.Api.World.GetBlock(new AssetLocation(Lantern))!.BlockId;
        World.Api.World.GetBlockAccessor(synchronize: false, relight: false, strict: false).SetBlock(lanternId, lantern);
        await World.Ticks(20);
        Assert.Equal(0, BlockLight(beside));

        await Ok("/atlasfx2 relight-region relight_raw 520 6 520 520 6 520");

        await World.Until(() => BlockLight(beside) == 20, timeoutTicks: 600);
        Assert.Equal(0, World.Api.World.BlockAccessor.GetLightLevel(beside, EnumLightLevelType.OnlySunLight));
    }

    [AtlasScenario]
    public async Task RelightRegion_Should_KeepBlockEntities_When_RestoringTheirLight()
    {
        int dimId = await PlayerInRoofedDimension("relightd", "relight_be");
        var accessor = World.Api.World.BlockAccessor;

        // A light source that owns a block entity: RelightRegion writes to this one.
        var lamp = new BlockPos(520, 6, 520, dimId);
        var besideLamp = new BlockPos(521, 6, 520, dimId);
        World.SetBlock("game:lantern-small-up", lamp);
        await World.Until(() => BlockLight(besideLamp) > 0, timeoutTicks: 600);
        int lampLight = BlockLight(besideLamp);
        BlockEntity lampEntity = accessor.GetBlockEntity(lamp)!;
        Assert.NotNull(lampEntity);
        byte[] lampState = StateOf(lampEntity);

        // A container with contents: RelightRegion never writes to it (it emits no light), so the
        // write Manifold uses is replayed on it by hand to prove it leaves a container alone.
        var chest = new BlockPos(524, 5, 520, dimId);
        await PlaceChest(chest, "flint", 7);
        BlockEntity chestEntity = accessor.GetBlockEntity(chest)!;
        World.Api.World.GetBlockAccessor(synchronize: false, relight: true, strict: false)
            .ExchangeBlock(accessor.GetBlock(chest).BlockId, chest);

        await Ok("/atlasfx2 relight-region relight_be 516 5 516 526 7 526");
        await Task.Delay(Settle);
        await World.Ticks(5);

        Assert.Same(chestEntity, accessor.GetBlockEntity(chest));
        AssertChestHolds(chest, "flint", 7);
        Assert.Same(lampEntity, accessor.GetBlockEntity(lamp));
        Assert.Equal(lampState, StateOf(lampEntity));
        Assert.Equal(lampLight, BlockLight(besideLamp));
    }

    private static byte[] StateOf(BlockEntity entity)
    {
        var tree = new TreeAttribute();
        entity.ToTreeAttributes(tree);
        return tree.ToBytes();
    }

    private async Task<int> PlayerInRoofedDimension(string playerName, string path) =>
        await EnterRoofedDimension(await JoinSurvivalPlayer(playerName), path);

    private async Task<int> EnterRoofedDimension(ITestPlayer player, string path)
    {
        await Ok($"/atlasfx2 create-darksky {path} 40");
        int dimId = await DimensionId(path);
        await Ok($"/atlasfx teleport-player {player.Player.PlayerName} {path}");
        await LandedAt(player, dimId, 512, 512);

        // The engine's lighting gate: it opens once the overworld column under the player loads.
        await World.Until(() => World.Api.WorldManager.GetMapChunk(16, 16) != null, timeoutTicks: 1200);
        return dimId;
    }

    private int BlockLight(BlockPos pos) =>
        World.Api.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight);
}
