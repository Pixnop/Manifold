namespace Manifold.Scenarios.CompatDowngrade;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Shared helpers for the two verifier scenarios (see README.md): each loads one of the fixtures
/// tests/Manifold.Scenarios.CompatFixtures built (a real save/load round trip through a
/// DIFFERENT Manifold build than the one that wrote it) and asserts on what survived. Talks to
/// the compat dimension only through /manicompat commands and SaveGame data, the same boundary
/// ManifoldScenarioBase uses in Manifold.Scenarios.
/// </summary>
public abstract class CompatVerifyScenarioBase : AtlasScenarioBase
{
    protected async Task<int> CompatDimensionId()
    {
        await World.Until(() => ReadDimensionId() is not null, timeoutTicks: 200);
        return ReadDimensionId()!.Value;
    }

    /// <summary>
    /// The dimension id "compat" was published under on the OTHER build's boot, snapshotted by
    /// CompatFixtureModSystem before this boot's own RegisterStatic overwrites it. Non-null by
    /// construction: both fixtures always carry a prior publish.
    /// </summary>
    protected int PreviousDimensionId()
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("manicompat:prevdimid");
        Assert.NotNull(data);
        return System.BitConverter.ToInt32(data!, 0);
    }

    protected BlockPos ReadPosition(string key, int dimension)
    {
        byte[]? x = World.Api.WorldManager.SaveGame.GetData($"manicompat:{key}:x");
        byte[]? y = World.Api.WorldManager.SaveGame.GetData($"manicompat:{key}:y");
        byte[]? z = World.Api.WorldManager.SaveGame.GetData($"manicompat:{key}:z");
        Assert.True(x is { Length: 4 } && y is { Length: 4 } && z is { Length: 4 }, $"Missing published position '{key}'.");
        return new BlockPos(
            System.BitConverter.ToInt32(x!, 0),
            System.BitConverter.ToInt32(y!, 0),
            System.BitConverter.ToInt32(z!, 0),
            dimension);
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
                && System.Math.Abs(player.Position.X - x) <= 1
                && System.Math.Abs(player.Position.Z - z) <= 1,
            timeoutTicks: 600);

    protected static int HotbarCount(ITestPlayer player, string code)
    {
        IInventory hotbar = player.Player.InventoryManager.GetHotbarInventory();
        int total = 0;
        foreach (ItemSlot slot in hotbar)
        {
            if (slot.Itemstack?.Collectible?.Code?.ToString() == code)
            {
                total += slot.Itemstack.StackSize;
            }
        }

        return total;
    }

    private int? ReadDimensionId()
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("manicompat:dimid");
        return data is null ? null : System.BitConverter.ToInt32(data, 0);
    }
}
