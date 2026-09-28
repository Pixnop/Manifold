using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class GeneratedColumnStoreTests
{
    [Fact]
    public void IsGenerated_Should_Be_False_For_Unknown_Column()
    {
        var store = new GeneratedColumnStore();
        Assert.False(store.IsGenerated(10, 5, 7));
    }

    [Fact]
    public void MarkGenerated_Should_Make_Column_Known_And_Set_Dirty()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        Assert.True(store.IsGenerated(10, 5, 7));
        Assert.True(store.IsDirty);
    }

    [Fact]
    public void MarkGenerated_Should_Distinguish_Dimensions_And_Coords()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        Assert.False(store.IsGenerated(11, 5, 7)); // different dim
        Assert.False(store.IsGenerated(10, 6, 7)); // different cx
        Assert.False(store.IsGenerated(10, 5, 8)); // different cz
    }

    [Fact]
    public void ClearDirty_Should_Reset_Dirty_Flag()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        store.ClearDirty();
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void ToBytes_LoadFromBytes_Should_Roundtrip_The_Set()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        store.MarkGenerated(11, 100, 2000);
        store.MarkGenerated(1023, 2097151, 2097151); // max coords + max dim

        var bytes = store.ToBytes();

        var restored = new GeneratedColumnStore();
        restored.LoadFromBytes(bytes);

        Assert.True(restored.IsGenerated(10, 5, 7));
        Assert.True(restored.IsGenerated(11, 100, 2000));
        Assert.True(restored.IsGenerated(1023, 2097151, 2097151));
        Assert.False(restored.IsGenerated(10, 5, 8));
        Assert.False(restored.IsDirty); // load clears dirty
    }

    [Fact]
    public void LoadFromBytes_Should_Handle_Null_And_Empty()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);

        store.LoadFromBytes(null);
        Assert.False(store.IsGenerated(10, 5, 7));

        store.MarkGenerated(10, 5, 7);
        store.LoadFromBytes(System.Array.Empty<byte>());
        Assert.False(store.IsGenerated(10, 5, 7));
    }

    [Fact]
    public void ToBytes_Should_Match_The_0_5_1_Released_Format()
    {
        // Golden bytes: the wire format is 8 bytes per key, little-endian, packed as
        // (dim << 42) | (cx << 21) | cz (see the class remarks) - pinned independently of ToBytes
        // itself so a format change here is caught even if the writer and this assertion drifted
        // together. Unchanged since v0.5.1 (git show v0.5.1:src/Manifold/Internal/GeneratedColumnStore.cs).
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);

        long expectedKey = (10L << 42) | (5L << 21) | 7L;
        Assert.Equal(System.BitConverter.GetBytes(expectedKey), store.ToBytes());
    }

    [Fact]
    public void LoadFromBytes_Should_Read_A_Blob_With_No_Sidecar_Entry_As_Version_1()
    {
        var store = new GeneratedColumnStore();
        store.LoadFromBytes(new GeneratedColumnStore().ToBytes()); // version defaults to 1
        Assert.False(store.IsVersionRefused);
    }

    [Fact]
    public void LoadFromBytes_Should_Refuse_A_Schema_Version_Newer_Than_Supported()
    {
        var seed = new GeneratedColumnStore();
        seed.MarkGenerated(10, 5, 7);

        var store = new GeneratedColumnStore();
        store.LoadFromBytes(seed.ToBytes(), version: 99);

        Assert.True(store.IsVersionRefused);
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void IsGenerated_Should_Fail_Closed_When_The_Version_Is_Refused()
    {
        // A refused load has no reliable record of which columns were generated: every column
        // must read as already generated (loaded from disk, never regenerated) so nothing
        // regenerates over player modifications made under the newer version.
        var store = new GeneratedColumnStore();
        store.LoadFromBytes(null, version: 99);

        Assert.True(store.IsGenerated(1, 2, 3));
        Assert.True(store.IsGenerated(999, -1, -1));
    }

    [Fact]
    public void LoadFromBytes_Should_Clear_IsVersionRefused_On_A_Later_Accepted_Load()
    {
        var store = new GeneratedColumnStore();
        store.LoadFromBytes(null, version: 99);
        Assert.True(store.IsVersionRefused);

        store.LoadFromBytes(null);

        Assert.False(store.IsVersionRefused);
    }

    [Fact]
    public void RemoveDimension_Should_Drop_Only_That_Dimensions_Columns()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        store.MarkGenerated(10, 6, 8);
        store.MarkGenerated(11, 5, 7);

        store.RemoveDimension(10);

        Assert.False(store.IsGenerated(10, 5, 7));
        Assert.False(store.IsGenerated(10, 6, 8));
        Assert.True(store.IsGenerated(11, 5, 7)); // other dimension untouched
    }

    [Fact]
    public void RemoveDimension_Should_Set_Dirty_When_Something_Removed()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        store.ClearDirty();

        store.RemoveDimension(10);

        Assert.True(store.IsDirty);
    }

    [Fact]
    public void RemoveDimension_Should_Not_Set_Dirty_When_Nothing_Removed()
    {
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 5, 7);
        store.ClearDirty();

        store.RemoveDimension(999); // unknown dimension

        Assert.False(store.IsDirty);
        Assert.True(store.IsGenerated(10, 5, 7));
    }

    [Fact]
    public void RemoveDimension_Should_Not_Affect_Columns_Sharing_Coords_In_Other_Dims()
    {
        // A reused engine id must not inherit the prior occupant's column markers, but a
        // different live dimension that happens to share coords must survive the prune.
        var store = new GeneratedColumnStore();
        store.MarkGenerated(10, 0, 0);
        store.MarkGenerated(1023, 0, 0);

        store.RemoveDimension(10);

        Assert.False(store.IsGenerated(10, 0, 0));
        Assert.True(store.IsGenerated(1023, 0, 0));
    }
}
