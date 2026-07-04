namespace Manifold.Scenarios;

using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

[Trait("Category", "E2E")]
public class MultiPlayerScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Players_Should_KeepIsolatedInventoriesAndDimensions_When_TransitingConcurrently()
    {
        int vaultId = await DimensionId("vault");
        int flatId = await DimensionId("flat");

        ITestPlayer alice = await World.JoinPlayer("atlas_alice");
        ITestPlayer bob = await World.JoinPlayer("atlas_bob");
        await alice.GiveItem("game:stick", 5);
        await bob.GiveItem("game:stick", 7);

        // Alice enters the separate-inventory vault; Bob stays in the overworld.
        CommandResult toVault = await World.ExecuteCommand("/atlasfx teleport-player atlas_alice vault");
        Assert.True(toVault.Ok, toVault.Message);
        await World.Until(() => alice.Position.dimension == vaultId, timeoutTicks: 600);
        await World.Until(() => HotbarCount(alice, "game:stick") == 0, timeoutTicks: 200);

        // Alice's swap must not touch Bob.
        Assert.Equal(7, HotbarCount(bob, "game:stick"));
        Assert.Equal(0, bob.Position.dimension);

        // Bob transits to flat: two players in two different custom dimensions at the same coordinates.
        CommandResult toFlat = await World.ExecuteCommand("/atlasfx teleport-player atlas_bob flat");
        Assert.True(toFlat.Ok, toFlat.Message);
        await World.Until(() => bob.Position.dimension == flatId, timeoutTicks: 600);
        Assert.Equal(7, HotbarCount(bob, "game:stick"));

        // Dimension-aware queries see exactly the right player in each dimension. Entity-partitioning
        // catches up to a just-applied ChangeDimension/teleport on a later tick, so poll instead of
        // asserting immediately (same pattern EntityTransitScenarios uses for its arrival check).
        var vaultArea = new BlockPos(512, 6, 512, vaultId).Area(16);
        var flatArea = new BlockPos(512, 6, 512, flatId).Area(16);
        await World.Until(
            () => World.EntitiesIn(vaultArea).Any(e => e.EntityId == alice.Entity.EntityId),
            timeoutTicks: 200);
        await World.Until(
            () => World.EntitiesIn(flatArea).Any(e => e.EntityId == bob.Entity.EntityId),
            timeoutTicks: 200);

        var vaultPlayers = World.EntitiesIn(vaultArea).OfType<EntityPlayer>().ToList();
        var flatPlayers = World.EntitiesIn(flatArea).OfType<EntityPlayer>().ToList();
        Assert.Equal(alice.Entity.EntityId, Assert.Single(vaultPlayers).EntityId);
        Assert.Equal(bob.Entity.EntityId, Assert.Single(flatPlayers).EntityId);

        // Alice returns: her sticks come back, Bob is unaffected in flat.
        CommandResult back = await World.ExecuteCommand("/atlasfx teleport-player atlas_alice overworld");
        Assert.True(back.Ok, back.Message);
        await World.Until(() => alice.Position.dimension == 0, timeoutTicks: 600);
        await World.Until(() => HotbarCount(alice, "game:stick") == 5, timeoutTicks: 200);
        Assert.Equal(7, HotbarCount(bob, "game:stick"));
        Assert.Equal(flatId, bob.Position.dimension);
    }
}
