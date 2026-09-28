using Manifold.Internal;
using NSubstitute;
using Vintagestory.API.Common;
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
    public void LoadFromBytes_Should_Log_Warning_On_Corrupt_Data()
    {
        var store = new PlayerPositionStore();
        var logger = Substitute.For<ILogger>();

        // The corrupt-data recovery (start fresh) is otherwise silent; it must log.
        store.LoadFromBytes(new byte[] { 0xFF, 0x01, 0x02 }, logger);

        logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void ToBytes_Should_Match_The_0_5_1_Released_Format()
    {
        // Golden bytes: BinaryWriter's own encoding of (count:int, then per entry key:string,
        // x:int, y:int, z:int), pinned independently of ToBytes itself, hand-built the same way
        // BinaryWriter.Write(string) always has (7-bit length prefix + UTF8 bytes). Unchanged
        // since v0.5.1 (git show v0.5.1:src/Manifold/Internal/PlayerPositionStore.cs).
        var store = new PlayerPositionStore();
        store.Record("alice", 10, 1, 2, 3);

        using var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1);
            w.Write("alice|10");
            w.Write(1);
            w.Write(2);
            w.Write(3);
        }

        Assert.Equal(ms.ToArray(), store.ToBytes());
    }

    [Fact]
    public void LoadFromBytes_Should_Read_A_Blob_With_No_Sidecar_Entry_As_Version_1()
    {
        var store = new PlayerPositionStore();
        store.LoadFromBytes(new PlayerPositionStore().ToBytes()); // version defaults to 1
        Assert.False(store.IsVersionRefused);
    }

    [Fact]
    public void LoadFromBytes_Should_Refuse_A_Schema_Version_Newer_Than_Supported()
    {
        var seed = new PlayerPositionStore();
        seed.Record("alice", 10, 1, 2, 3);

        var store = new PlayerPositionStore();
        store.LoadFromBytes(seed.ToBytes(), version: 99);

        Assert.True(store.IsVersionRefused);
        Assert.False(store.TryGet("alice", 10, out _, out _, out _));
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void LoadFromBytes_Should_Not_Refuse_An_Unrecognized_Version_When_The_Blob_Is_Absent()
    {
        // An unrecognized version with nothing to refuse (e.g. a future release that moved this
        // data to another key) must not latch a refusal for the rest of the session.
        var store = new PlayerPositionStore();
        store.LoadFromBytes(null, version: 99);

        Assert.False(store.IsVersionRefused);

        store.LoadFromBytes(System.Array.Empty<byte>(), version: 99);

        Assert.False(store.IsVersionRefused);
    }

    [Fact]
    public void LoadFromBytes_Should_Clear_IsVersionRefused_On_A_Later_Accepted_Load()
    {
        var seed = new PlayerPositionStore();
        seed.Record("alice", 10, 1, 2, 3);

        var store = new PlayerPositionStore();
        store.LoadFromBytes(seed.ToBytes(), version: 99);
        Assert.True(store.IsVersionRefused);

        store.LoadFromBytes(null);

        Assert.False(store.IsVersionRefused);
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
