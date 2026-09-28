namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// The "colgen" dimension is never pregenerated at boot (see AtlasFixtureModSystem), so its one
/// column only ever generates through the /atlasfx pregen call below: after the fixture's
/// ColumnGenerated subscriber (registered at boot) is already live.
/// </summary>
[Trait("Category", "E2E")]
public class ColumnGeneratedEventScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task ColumnGenerated_Should_LetSubscriberPlaceAMarker_When_ColumnIsGenerated()
    {
        int colgenId = await DimensionId("colgen");

        // Center of the single column GenerateRegion(radius 0) produces around the fixed spawn
        // (512, 8, 512): chunk (16, 16), so world (528, _, 528); matches the fixture's marker
        // subscriber (AtlasFixtureModSystem.Coverage.cs).
        var markerPos = new BlockPos(528, 10, 528, colgenId);

        await Ok("/atlasfx pregen colgen");

        await BlockBecomes(markerPos, "game:rock-basalt", timeoutTicks: 1200);
    }
}
