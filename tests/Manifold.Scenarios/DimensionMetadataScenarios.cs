namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// Registration-time metadata read back through IDimension.Metadata after a real boot. Read-only
/// against registry state, so the class shares its world: neither rollback nor recycle is needed.
/// Metadata persistence semantics across a server restart (static dimensions re-declare, runtime
/// ones lose theirs) are deliberately NOT covered: they would need Atlas to reboot the SAME world
/// (savegame kept, host recycled), which no isolation mode offers today.
/// </summary>
[Trait("Category", "E2E")]
public class DimensionMetadataScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Dimension_Should_ExposeTypedMetadata_When_Registered()
    {
        await DimensionId("flat");

        CommandResult label = await World.ExecuteCommand("/atlasfx metadata flat fixture-label");
        Assert.True(label.Ok, label.Message);
        Assert.Equal("String:granite-slab", label.Message);

        CommandResult level = await World.ExecuteCommand("/atlasfx metadata flat fixture-level");
        Assert.True(level.Ok, level.Message);
        Assert.Equal("Int32:3", level.Message);
    }

    [AtlasScenario]
    public async Task Dimension_Should_ReportNoMetadata_When_KeyIsUnknown()
    {
        await DimensionId("flat");

        CommandResult missing = await World.ExecuteCommand("/atlasfx metadata flat no-such-key");
        Assert.False(missing.Ok, "An unknown metadata key was reported as present.");
        Assert.Equal("missing", missing.Message);

        // A dimension registered without metadata has an empty table, not a null one.
        CommandResult voidKey = await World.ExecuteCommand("/atlasfx metadata void fixture-label");
        Assert.False(voidKey.Ok, "A metadata-free dimension reported a value.");
        Assert.Equal("missing", voidKey.Message);
    }
}
