namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Boot-level smoke checks: the embedded server starts with Manifold staged,
/// the ModLoader accepts it, and the world is functional. Nothing else in this
/// suite matters until these pass.
/// </summary>
[Trait("Category", "E2E")]
public class SmokeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Server_Should_LoadManifold_When_Booting()
    {
        Assert.True(
            World.Api.ModLoader.IsModEnabled("manifold"),
            "The manifold mod is not enabled in the embedded server.");

        // The ModSystem must have been discovered inside the staged assembly.
        // Looked up by name: scenario code must not reference Manifold types
        // (the ModLoader loads its own copy of Manifold.dll).
        Assert.NotNull(World.Api.ModLoader.GetModSystem("Manifold.ManifoldModSystem"));

        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task World_Should_AcceptBlockWrites_When_ManifoldIsLoaded()
    {
        BlockPos pos = World.Spawn.Offset(1, 1, 0);
        World.SetBlock("game:chest-east", pos);
        await World.Ticks(5);

        Assert.Equal("game:chest-east", World.BlockAt(pos).Code.ToString());
    }
}
