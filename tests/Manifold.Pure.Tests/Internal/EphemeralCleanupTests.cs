using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class EphemeralCleanupTests
{
    [Fact]
    public void RemoveAll_Removes_Only_Ephemeral_Dimensions()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(new AssetLocation("owner:keep_persistent"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Persistent()
            .RegisterStatic();
        registry.DefineForOwner(new AssetLocation("owner:eph_a"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();
        registry.DefineForOwner(new AssetLocation("owner:eph_b"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();

        int removed = EphemeralCleanup.RemoveAll(registry);

        Assert.Equal(2, removed);
        Assert.NotNull(registry.Get(new AssetLocation("owner:keep_persistent")));
        Assert.NotNull(registry.Get(new AssetLocation("manifold:overworld"))); // built-in untouched
        Assert.Null(registry.Get(new AssetLocation("owner:eph_a")));
        Assert.Null(registry.Get(new AssetLocation("owner:eph_b")));
    }

    [Fact]
    public void RemoveAll_Fires_Destroyed_For_Each_Removed_Dimension()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(new AssetLocation("owner:eph_a"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();
        registry.DefineForOwner(new AssetLocation("owner:eph_b"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();

        var destroyedCodes = new List<string>();
        registry.Destroyed += (_, e) => destroyedCodes.Add(e.Dimension.Code.ToString());

        EphemeralCleanup.RemoveAll(registry);

        Assert.Equal(2, destroyedCodes.Count);
        Assert.Contains("owner:eph_a", destroyedCodes);
        Assert.Contains("owner:eph_b", destroyedCodes);
    }

    [Fact]
    public void RemoveAll_Returns_Zero_When_No_Ephemeral_Dims_Exist()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(new AssetLocation("owner:keep_persistent"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Persistent()
            .RegisterStatic();

        int removed = EphemeralCleanup.RemoveAll(registry);

        Assert.Equal(0, removed);
    }

    [Fact]
    public void RemoveAll_Throws_When_Registry_Is_Null()
    {
        Assert.Throws<System.ArgumentNullException>(() => EphemeralCleanup.RemoveAll(null!));
    }

    private static DimensionRegistry NewRegistry()
    {
        return new DimensionRegistry(new DimensionAllocator());
    }
}
