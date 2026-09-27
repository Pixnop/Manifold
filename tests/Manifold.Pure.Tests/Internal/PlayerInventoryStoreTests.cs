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

        var restored = PlayerInventoryStore.TryFromBytes(store.ToBytes());

        Assert.NotNull(restored);
        Assert.Equal("mod:vault", restored!.CurrentKey(ManifoldInventory.Backpack));
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.GetSnapshot(ManifoldInventory.Backpack, "shared"));
        Assert.Equal(new byte[] { 9 }, restored.GetSnapshot(ManifoldInventory.Hotbar, "mod:vault"));
        Assert.Null(restored.GetSnapshot(ManifoldInventory.Character, "shared"));
    }

    [Fact]
    public void TryFromBytes_Should_Handle_Null_And_Empty()
    {
        Assert.Equal(InventoryProfileResolver.SharedKey, PlayerInventoryStore.TryFromBytes(null)!.CurrentKey(ManifoldInventory.Hotbar));
        Assert.Equal(InventoryProfileResolver.SharedKey, PlayerInventoryStore.TryFromBytes(System.Array.Empty<byte>())!.CurrentKey(ManifoldInventory.Hotbar));
    }

    [Fact]
    public void TryFromBytes_Should_Return_Null_On_Corrupt_Data()
    {
        // Corrupt moddata must be reported, not silently swapped for an empty store - the caller
        // (TransitService.ApplyInventoryPolicy) relies on null to skip the swap and preserve the
        // raw bytes instead of overwriting them with an empty store.
        Assert.Null(PlayerInventoryStore.TryFromBytes(new byte[] { 0xFF, 0x01, 0x02 }));
    }
}
