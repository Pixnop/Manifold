namespace Manifold.Scenarios.CompatFixtures;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Shared helpers for the two fixture-builder scenarios (see README.md): each is an ordinary
/// [AtlasScenario] whose only job is to leave the world in the state its matching
/// Manifold.Scenarios.Compat verifier scenario expects, for `atlas fixture` to harvest. Talks to
/// the compat dimension only through /manicompat commands and SaveGame data, the same boundary
/// ManifoldScenarioBase uses in Manifold.Scenarios (scenario code cannot share assembly identity
/// with whichever Manifold.dll the ModLoader staged).
/// </summary>
public abstract class CompatFixtureScenarioBase : AtlasScenarioBase
{
    protected async Task<int> CompatDimensionId()
    {
        await World.Until(() => ReadDimensionId() is not null, timeoutTicks: 200);
        return ReadDimensionId()!.Value;
    }

    protected async Task<CommandResult> Ok(string command)
    {
        CommandResult r = await World.ExecuteCommand(command);
        Assert.True(r.Ok, r.Message);
        return r;
    }

    protected Task LandedAt(ITestPlayer player, int dimension, int x, int z) =>
        World.Until(
            () => player.Position.dimension == dimension
                && Math.Abs(player.Position.X - x) <= 1
                && Math.Abs(player.Position.Z - z) <= 1,
            timeoutTicks: 600);

    /// <summary>
    /// Records a position under a fixed SaveGame key so the verifier scenario, running against a
    /// different Manifold build entirely, can assert the exact coordinates without sharing any
    /// code with this project.
    /// </summary>
    protected void PublishPosition(string key, BlockPos pos)
    {
        World.Api.WorldManager.SaveGame.StoreData($"manicompat:{key}:x", System.BitConverter.GetBytes(pos.X));
        World.Api.WorldManager.SaveGame.StoreData($"manicompat:{key}:y", System.BitConverter.GetBytes(pos.Y));
        World.Api.WorldManager.SaveGame.StoreData($"manicompat:{key}:z", System.BitConverter.GetBytes(pos.Z));
    }

    private int? ReadDimensionId()
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("manicompat:dimid");
        return data is null ? null : System.BitConverter.ToInt32(data, 0);
    }
}
