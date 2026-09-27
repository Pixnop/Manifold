namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

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

    /// <summary>
    /// The fixture's ordered event log ("entering:flat", "left:overworld->flat", "destroyed:x"...),
    /// oldest first. Scenarios that assert on it clear it first: the log is world state, shared by
    /// every scenario of a class that does not isolate.
    /// </summary>
    protected IReadOnlyList<string> EventLog()
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("atlasfixture:eventlog");
        return data is null or { Length: 0 } ? [] : System.Text.Encoding.UTF8.GetString(data).Split('|');
    }

    protected void ClearEventLog() => World.Api.WorldManager.SaveGame.StoreData("atlasfixture:eventlog", []);

    /// <summary>Runs a command and asserts it succeeded, reporting the failure message on the assertion itself.</summary>
    protected async Task<CommandResult> Ok(string command)
    {
        CommandResult r = await World.ExecuteCommand(command);
        Assert.True(r.Ok, r.Message);
        return r;
    }

    /// <summary>Waits until the block at <paramref name="pos"/> becomes <paramref name="code"/>.</summary>
    protected Task BlockBecomes(BlockPos pos, string code, int timeoutTicks = 600) =>
        World.Until(() => World.BlockAt(pos).Code?.ToString() == code, timeoutTicks);

    /// <summary>Waits until an entity with <paramref name="e"/>'s id is found within 16 blocks of <paramref name="at"/>.</summary>
    protected Task EntityReaches(Entity e, BlockPos at) =>
        World.Until(() => World.EntitiesIn(at.Area(16)).Any(x => x.EntityId == e.EntityId));

    /// <summary>Places a chest at <paramref name="at"/> and puts <paramref name="count"/> of <paramref name="item"/> in its first slot.</summary>
    protected async Task PlaceChest(BlockPos at, string item, int count)
    {
        World.SetBlock("game:chest-east", at);
        await World.Ticks(2);

        var container = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(at), exactMatch: false);
        var stack = new ItemStack(World.Api.World.GetItem(new AssetLocation("game", item)), count);
        container.Inventory[0]!.Itemstack = stack;
        container.Inventory[0]!.MarkDirty();
        await World.Ticks(2);
    }

    /// <summary>Asserts the chest at <paramref name="at"/> holds <paramref name="count"/> of <paramref name="item"/> in its first slot.</summary>
    protected void AssertChestHolds(BlockPos at, string item, int count)
    {
        var container = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(at), exactMatch: false);
        ItemStack? stack = container.Inventory[0]!.Itemstack;
        Assert.NotNull(stack);
        Assert.Equal(count, stack!.StackSize);
        Assert.Equal($"game:{item}", stack.Collectible.Code.ToString());
    }

    /// <summary>Joins a player and pins them to survival, whatever the world's play style defaults to.</summary>
    protected async Task<ITestPlayer> JoinSurvivalPlayer(string name)
    {
        ITestPlayer player = await World.JoinPlayer(name);
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Player.BroadcastPlayerData(true);
        return player;
    }

    /// <summary>Waits until the player stands within a block of the given X/Z in the given dimension.</summary>
    protected Task LandedAt(ITestPlayer player, int dimension, int x, int z) =>
        World.Until(
            () => player.Position.dimension == dimension
                && Math.Abs(player.Position.X - x) <= 1
                && Math.Abs(player.Position.Z - z) <= 1,
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
}
