namespace Manifold.Scenarios;

using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

[Trait("Category", "E2E")]
public class PersistenceScenarios : ManifoldScenarioBase
{
    /// <summary>
    /// Boots, shuts the server down for real, and boots again on the persisted world
    /// (Atlas RestartWorld) before the body runs: the assertions below therefore hold
    /// against a world that made a genuine save/load round trip.
    /// </summary>
    [AtlasScenario(RestartWorld = true)]
    public async Task PersistentDimensions_Should_KeepIdsAndTerrain_When_ServerRestarts()
    {
        // The fixture increments this on every StartServerSide: proves the restart was real.
        Assert.True(ReadInt("atlasfixture:bootcount") >= 2, "the server did not actually restart");

        // Manifest seeding: a persistent dimension re-registered after the restart keeps the
        // engine id it was allocated on the first boot.
        foreach (string path in new[] { "flat", "void", "vault", "meta" })
        {
            int current = await DimensionId(path);
            int? firstBoot = ReadInt("atlasfixture:dimid-firstboot:" + path);
            Assert.NotNull(firstBoot);
            Assert.Equal(firstBoot!.Value, current);
        }

        // Generated terrain survived: the flat spawn slab is there without a fresh generation
        // pass (the generated-column store marks it done; the region is loaded from disk).
        int flatId = await DimensionId("flat");
        var probe = new BlockPos(512, 3, 512, flatId);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);
    }
}
