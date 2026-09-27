namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

[Trait("Category", "E2E")]
public class ReconnectScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Player_Should_RejoinInTheOverworld_When_KickedInsideAnEphemeralThatIsThenRemoved()
    {
        await World.ExecuteCommand("/atlasfx create-ephemeral reconnectghost");
        int ghostId = await DimensionId("reconnectghost");

        ITestPlayer player = await World.JoinPlayer("atlas_reconn1");
        int homeX = player.Position.X;
        int homeZ = player.Position.Z;

        await World.ExecuteCommand("/atlasfx teleport-player atlas_reconn1 reconnectghost");
        await World.Until(() => player.Position.dimension == ghostId, timeoutTicks: 600);

        await World.ExecuteCommand("/atlasfx2 kick atlas_reconn1");
        await World.Until(() => !player.IsConnected, timeoutTicks: 200);

        await Ok("/atlasfx remove reconnectghost");

        // The dimension they were kicked in no longer exists: the reconnect must not strand them
        // in the void, but land them back where the overworld last saw them (Manifold's rescue
        // uses LastVisited). Same name is free to rejoin: it is no longer connected, only kicked.
        ITestPlayer rejoined = await World.JoinPlayer("atlas_reconn1");
        await LandedAt(rejoined, dimension: 0, x: homeX, z: homeZ);
    }

    [AtlasScenario]
    public async Task Player_Should_KeepVaultItemsAcrossAKick_And_RestoreOverworldItemsOnReturn()
    {
        int vaultId = await DimensionId("vault");
        ITestPlayer player = await World.JoinPlayer("atlas_reconn2");
        await player.GiveItem("game:stick", 4);
        Assert.Equal(4, HotbarCount(player, "game:stick"));

        await World.ExecuteCommand("/atlasfx teleport-player atlas_reconn2 vault");
        await World.Until(() => player.Position.dimension == vaultId, timeoutTicks: 600);
        await World.Until(() => HotbarCount(player, "game:stick") == 0, timeoutTicks: 200);

        await player.GiveItem("game:flint", 2);
        Assert.Equal(2, HotbarCount(player, "game:flint"));

        await World.ExecuteCommand("/atlasfx2 kick atlas_reconn2");
        await World.Until(() => !player.IsConnected, timeoutTicks: 200);

        // The vault dimension is still Active (never removed): the reconnect lands straight back
        // in it, with the vault-only items untouched and the overworld set still swapped out.
        ITestPlayer rejoined = await World.JoinPlayer("atlas_reconn2");
        await World.Until(() => rejoined.Position.dimension == vaultId, timeoutTicks: 600);
        Assert.Equal(2, HotbarCount(rejoined, "game:flint"));
        Assert.Equal(0, HotbarCount(rejoined, "game:stick"));

        await World.ExecuteCommand("/atlasfx teleport-player atlas_reconn2 overworld");
        await World.Until(() => rejoined.Position.dimension == 0, timeoutTicks: 600);
        await World.Until(() => HotbarCount(rejoined, "game:stick") == 4, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(rejoined, "game:flint"));
    }
}
