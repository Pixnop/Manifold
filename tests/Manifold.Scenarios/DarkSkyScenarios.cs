namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// /atlasfx2 create-darksky against the real engine: the sealed ceiling over every generated
/// column, with the granite slab and the open air pocket between it and the cap intact.
/// </summary>
[Trait("Category", "E2E")]
public class DarkSkyScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task DarkSkyDimension_Should_SealCeilingOnGeneratedColumns_When_Created()
    {
        const int ceilingY = 40;
        await Ok($"/atlasfx2 create-darksky dsky1 {ceilingY}");
        int dimId = await DimensionId("dsky1");

        // Spawn column (512,512) and its immediate neighbour (544,512): both fall inside the
        // radius-1 footprint that create-darksky pregenerates via the fixture's spawn teleport.
        foreach (int x in new[] { 512, 544 })
        {
            var cap = new BlockPos(x, ceilingY, 512, dimId);
            await BlockBecomes(cap, "game:rock-granite", timeoutTicks: 1200);

            var slab = new BlockPos(x, 3, 512, dimId);
            Assert.Equal("game:rock-granite", World.BlockAt(slab).Code.ToString());

            var gap = new BlockPos(x, 6, 512, dimId);
            Assert.Equal("game:air", World.BlockAt(gap).Code.ToString());
        }
    }
}
