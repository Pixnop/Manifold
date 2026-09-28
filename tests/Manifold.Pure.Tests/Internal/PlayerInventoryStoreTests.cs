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

    [Fact]
    public void ToBytes_Should_Match_The_0_5_1_Released_Format()
    {
        // Golden bytes: BinaryWriter's own encoding (currentKeys count, then per key: category
        // int + owner-key string; snapshots count, then per snapshot: key string + length int +
        // bytes), pinned independently of ToBytes itself. Unchanged since v0.5.1
        // (git show v0.5.1:src/Manifold/Internal/PlayerInventoryStore.cs).
        var store = new PlayerInventoryStore();
        store.SetCurrentKey(ManifoldInventory.Backpack, "mod:vault");
        store.SetSnapshot(ManifoldInventory.Backpack, "shared", new byte[] { 1, 2, 3 });

        using var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1);
            w.Write((int)ManifoldInventory.Backpack);
            w.Write("mod:vault");
            w.Write(1);
            w.Write(((int)ManifoldInventory.Backpack).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|shared");
            w.Write(3);
            w.Write(new byte[] { 1, 2, 3 });
        }

        Assert.Equal(ms.ToArray(), store.ToBytes());
    }

    [Fact]
    public void TryFromBytes_Should_Read_A_Blob_With_No_Sidecar_Entry_As_Version_1()
    {
        var restored = PlayerInventoryStore.TryFromBytes(new PlayerInventoryStore().ToBytes()); // version defaults to 1
        Assert.NotNull(restored);
    }

    [Fact]
    public void TryFromBytes_Should_Refuse_A_Schema_Version_Newer_Than_Supported()
    {
        var seed = new PlayerInventoryStore();
        seed.SetCurrentKey(ManifoldInventory.Backpack, "mod:vault");

        // A payload that IS valid for the current parser proves refusal is driven by the version
        // check, not by the corrupt-data catch block a garbage payload would also hit.
        Assert.Null(PlayerInventoryStore.TryFromBytes(seed.ToBytes(), version: 99));
    }
}
