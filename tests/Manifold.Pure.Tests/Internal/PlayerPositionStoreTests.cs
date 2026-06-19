using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class PlayerPositionStoreTests
{
    [Fact]
    public void TryGet_Should_Be_False_For_Unknown_Player()
    {
        var store = new PlayerPositionStore();
        Assert.False(store.TryGet("uid", 10, out _, out _, out _));
    }

    [Fact]
    public void Record_Then_TryGet_Should_Return_Position_And_Set_Dirty()
    {
        var store = new PlayerPositionStore();
        store.Record("uid", 10, 100, 64, 200);

        Assert.True(store.TryGet("uid", 10, out var x, out var y, out var z));
        Assert.Equal((100, 64, 200), (x, y, z));
        Assert.True(store.IsDirty);
    }

    [Fact]
    public void Record_Should_Distinguish_Player_And_Dimension()
    {
        var store = new PlayerPositionStore();
        store.Record("uid", 10, 1, 2, 3);

        Assert.False(store.TryGet("other", 10, out _, out _, out _)); // different player
        Assert.False(store.TryGet("uid", 11, out _, out _, out _));   // different dim
    }

    [Fact]
    public void Record_Should_Overwrite_Previous_Position()
    {
        var store = new PlayerPositionStore();
        store.Record("uid", 10, 1, 2, 3);
        store.Record("uid", 10, 9, 8, 7);

        store.TryGet("uid", 10, out var x, out var y, out var z);
        Assert.Equal((9, 8, 7), (x, y, z));
    }

    [Fact]
    public void ToBytes_LoadFromBytes_Should_Roundtrip()
    {
        var store = new PlayerPositionStore();
        store.Record("alice", 10, 1, 2, 3);
        store.Record("bob", 11, -100, 64, 5000);

        var bytes = store.ToBytes();
        var restored = new PlayerPositionStore();
        restored.LoadFromBytes(bytes);

        Assert.True(restored.TryGet("alice", 10, out var ax, out var ay, out var az));
        Assert.Equal((1, 2, 3), (ax, ay, az));
        Assert.True(restored.TryGet("bob", 11, out var bx, out var by, out var bz));
        Assert.Equal((-100, 64, 5000), (bx, by, bz));
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public void LoadFromBytes_Should_Handle_Null_Empty_And_Corrupt()
    {
        var store = new PlayerPositionStore();
        store.Record("uid", 10, 1, 2, 3);
        store.LoadFromBytes(null);
        Assert.False(store.TryGet("uid", 10, out _, out _, out _));

        store.Record("uid", 10, 1, 2, 3);
        store.LoadFromBytes(System.Array.Empty<byte>());
        Assert.False(store.TryGet("uid", 10, out _, out _, out _));

        store.LoadFromBytes(new byte[] { 0xFF, 0x01, 0x02 }); // corrupt - must not throw
        Assert.False(store.TryGet("uid", 10, out _, out _, out _));
    }

    [Fact]
    public void ClearDirty_Should_Reset_Flag()
    {
        var store = new PlayerPositionStore();
        store.Record("uid", 10, 1, 2, 3);
        store.ClearDirty();
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void RemoveDimension_Should_Drop_Only_That_Dimensions_Entries()
    {
        var store = new PlayerPositionStore();
        store.Record("alice", 10, 1, 2, 3);
        store.Record("bob", 10, 4, 5, 6);
        store.Record("alice", 11, 7, 8, 9);
        store.ClearDirty();

        store.RemoveDimension(10);

        Assert.False(store.TryGet("alice", 10, out _, out _, out _));
        Assert.False(store.TryGet("bob", 10, out _, out _, out _));
        Assert.True(store.TryGet("alice", 11, out _, out _, out _)); // other dim untouched
        Assert.True(store.IsDirty);
    }

    [Fact]
    public void RemoveDimension_Should_Not_Match_A_Different_Dim_With_A_Shared_Suffix()
    {
        var store = new PlayerPositionStore();
        store.Record("uid", 10, 1, 2, 3);
        store.Record("uid", 110, 4, 5, 6);

        store.RemoveDimension(10);

        Assert.False(store.TryGet("uid", 10, out _, out _, out _));
        Assert.True(store.TryGet("uid", 110, out _, out _, out _)); // 110 must not be matched by "|10"
    }
}
