namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;

/// <summary>
/// Shared helpers for reading the results the atlasfixture mod publishes through
/// SaveGame data. Command outcomes now come back directly from ExecuteCommand's
/// CommandResult; this side channel remains for boot-time state (dimension ids)
/// and events (player-entered/player-left), which are not command results and
/// cannot cross the assembly identity boundary any other way.
/// </summary>
public abstract class ManifoldScenarioBase : AtlasScenarioBase
{
    protected async Task<int> DimensionId(string path)
    {
        await World.Until(() => ReadDimensionId(path) is not null, timeoutTicks: 200);
        return ReadDimensionId(path)!.Value;
    }

    protected int? ReadDimensionId(string path)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("atlasfixture:dimid:" + path);
        return data is null ? null : BitConverter.ToInt32(data, 0);
    }

    protected bool FlagIsSet(string key)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData(key);
        return data is { Length: > 0 } && data[0] == 1;
    }

    protected int? ReadInt(string key)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData(key);
        return data is null ? null : BitConverter.ToInt32(data, 0);
    }

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
}
