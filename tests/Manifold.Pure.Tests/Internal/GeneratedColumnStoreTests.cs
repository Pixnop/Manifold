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
}
