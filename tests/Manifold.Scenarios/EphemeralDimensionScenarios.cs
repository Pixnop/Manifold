namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

// RollbackWorld (Atlas 0.8.0): the dimension created mid-scenario loads mini-dimension chunk
// columns, which are part of the snapshot since rollback stage 3, and Manifold rebuilds its
// in-memory registry, allocator, and stores from the restored SaveGame through the
// atlas:rollback:restored hook (ManifoldModSystem.OnAtlasRollbackRestored). Strict: no scenario
// here joins players or otherwise legitimately degrades the rollback, and the resync-proof pair
// below is only meaningful when the rollback actually happens.
[Trait("Category", "E2E")]
public class EphemeralDimensionScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task EphemeralDimension_Should_GenerateAndDisappear_When_CreatedThenRemoved()
    {
        CommandResult createResult = await World.ExecuteCommand("/atlasfx create-ephemeral temp1");
        Assert.True(createResult.Ok, "create-ephemeral reported failure.");
        int tempId = await DimensionId("temp1");
        Assert.InRange(tempId, 10, 1023);

        // Runtime-created dimension must produce worldgen output, same as boot-time ones.
        var probe = new BlockPos(512, 3, 512, tempId);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        CommandResult removeResult = await World.ExecuteCommand("/atlasfx remove temp1");
        Assert.True(removeResult.Ok, "remove reported failure.");
        Assert.Equal("removed", removeResult.Message);
    }

    // The two probes below are deliberately identical and share one dimension code: whichever
    // runs second on the class host re-creates a dimension whose first incarnation only a
    // rollback removed. Before the atlas:rollback:restored hook, this was the documented issue
    // #48 desync: the registry still held the first incarnation (re-registration refused as a
    // duplicate), the allocator still reserved its id, and GeneratedColumnStore still marked its
    // columns generated, so the re-created dimension would LOAD void instead of running worldgen.
    // Each probe asserts all three desyncs are gone: state reports unregistered, re-creation
    // succeeds, and the granite probe proves worldgen ran again over the rolled-back columns
    // (the id is reused deterministically, so stale markers would be fatal). Two identical
    // probes make the proof order-independent within the class.
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Registry_Should_ForgetRolledBackDimension_When_ProbedFirst()
        => await AssertRegistryForgotAndRecreates();

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Registry_Should_ForgetRolledBackDimension_When_ProbedSecond()
        => await AssertRegistryForgotAndRecreates();

    private async Task AssertRegistryForgotAndRecreates()
    {
        // The registry must not know the code: on the run after a sibling probe, only the
        // rollback (via Manifold's restored-hook resync) can have removed it.
        CommandResult state = await World.ExecuteCommand("/atlasfx state resurgent");
        Assert.False(state.Ok, "The registry still knows a dimension the rollback removed.");
        Assert.Equal("unregistered", state.Message);

        // The rolled-back SaveGame agrees: the previously published id is gone.
        Assert.Null(ReadDimensionId("resurgent"));

        // Re-registration under the same code must succeed (historically: refused as duplicate).
        CommandResult created = await World.ExecuteCommand("/atlasfx create-ephemeral resurgent");
        Assert.True(created.Ok, $"Re-creating the rolled-back dimension failed: {created.Message}");
        int id = await DimensionId("resurgent");
        Assert.InRange(id, 10, 1023);

        // Worldgen must run again over the rolled-back columns: a stale generated-columns store
        // would make the generator load the (now deleted) columns as void and this probe time out.
        var probe = new BlockPos(512, 3, 512, id);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);
    }
}
