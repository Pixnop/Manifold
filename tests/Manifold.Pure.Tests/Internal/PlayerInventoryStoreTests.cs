using Manifold.Api;
using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class PlayerInventoryStoreTests
{
    [Fact]
    public void CurrentKey_Should_Default_To_Shared()
    {
        var store = new PlayerInventoryStore();
        Assert.Equal(InventoryProfileResolver.SharedKey, store.CurrentKey(ManifoldInventory.Backpack));
    }

    [Fact]
    public void SetCurrentKey_And_Snapshot_Should_Roundtrip_Through_Bytes()
    {
        var store = new PlayerInventoryStore();
        store.SetCurrentKey(ManifoldInventory.Backpack, "mod:vault");
        store.SetSnapshot(ManifoldInventory.Backpack, "shared", new byte[] { 1, 2, 3 });
        store.SetSnapshot(ManifoldInventory.Hotbar, "mod:vault", new byte[] { 9 });

        var restored = PlayerInventoryStore.FromBytes(store.ToBytes());

        Assert.Equal("mod:vault", restored.CurrentKey(ManifoldInventory.Backpack));
        Assert.True(restored.HasSnapshot(ManifoldInventory.Backpack, "shared"));
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.GetSnapshot(ManifoldInventory.Backpack, "shared"));
        Assert.Equal(new byte[] { 9 }, restored.GetSnapshot(ManifoldInventory.Hotbar, "mod:vault"));
        Assert.False(restored.HasSnapshot(ManifoldInventory.Character, "shared"));
    }

    [Fact]
    public void FromBytes_Should_Handle_Null_Empty_And_Corrupt()
    {
        Assert.Equal(InventoryProfileResolver.SharedKey, PlayerInventoryStore.FromBytes(null).CurrentKey(ManifoldInventory.Hotbar));
        Assert.Equal(InventoryProfileResolver.SharedKey, PlayerInventoryStore.FromBytes(System.Array.Empty<byte>()).CurrentKey(ManifoldInventory.Hotbar));
        var corrupt = PlayerInventoryStore.FromBytes(new byte[] { 0xFF, 0x01, 0x02 });
        Assert.False(corrupt.HasSnapshot(ManifoldInventory.Hotbar, "shared"));
    }
}
