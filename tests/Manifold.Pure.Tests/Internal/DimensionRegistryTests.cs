using Manifold.Api;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// Tests for <see cref="DimensionRegistry"/> covering all core functionality.
/// </summary>
public sealed class DimensionRegistryTests
{
    [Fact]
    public void Registry_Should_Contain_Overworld_At_Construction()
    {
        var registry = NewRegistry();
        var ow = registry.Get(Code("manifold:overworld"));
        Assert.NotNull(ow);
        Assert.Equal(0, ow!.InternalId);
        Assert.True(ow.IsBuiltIn);
        Assert.Equal(DimensionLifetime.BuiltIn, ow.Lifetime);
        Assert.Equal(DimensionState.Active, ow.State);
    }

    [Fact]
    public void Define_Should_Throw_DimensionOwnerRequiredException()
    {
        var registry = NewRegistry();
        Assert.Throws<DimensionOwnerRequiredException>(() => registry.Define(Code("testmod:nether")));
    }

    [Fact]
    public void DefineForOwner_RegisterStatic_Should_Add_Dimension_To_Registry()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("testmod:nether"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        Assert.NotNull(dim);
        Assert.Equal(10, dim.InternalId);
        Assert.Equal("testmod", dim.OwnerModId);
        Assert.Same(dim, registry.Get(Code("testmod:nether")));
    }

    [Fact]
    public void DefineForOwner_Should_Throw_When_Code_Already_Registered_In_Same_Boot()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(Code("testmod:nether"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Throws<DimensionAlreadyRegisteredException>(() =>
            registry.DefineForOwner(Code("testmod:nether"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic());
    }

    [Fact]
    public void DefineForOwner_Should_Reject_Reserved_Manifold_Domain()
    {
        var registry = NewRegistry();
        Assert.Throws<System.ArgumentException>(() => registry.DefineForOwner(Code("manifold:foo"), "testmod"));
    }

    [Fact]
    public void All_Should_Include_Overworld_And_Registered_Dimensions()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(Code("a:b"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public void Created_Event_Should_Fire_After_RegisterStatic()
    {
        var registry = NewRegistry();
        IDimension? raised = null;
        registry.Created += (_, e) => raised = e.Dimension;

        registry.DefineForOwner(Code("a:b"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.NotNull(raised);
        Assert.Equal("a:b", raised!.Code.ToString());
    }

    [Fact]
    public void TryRemove_Should_Return_False_When_Code_Unknown()
    {
        var registry = NewRegistry();
        Assert.False(registry.TryRemove(Code("nope:nope")));
    }

    [Fact]
    public void TryRemove_Should_Throw_When_Target_Is_BuiltIn()
    {
        var registry = NewRegistry();
        Assert.Throws<DimensionBuiltInImmutableException>(
            () => registry.TryRemove(Code("manifold:overworld")));
    }

    [Fact]
    public void TryRemove_Should_Throw_When_Target_Is_Persistent()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(Code("a:b"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Throws<DimensionStateException>(() => registry.TryRemove(Code("a:b")));
    }

    [Fact]
    public void TryRemove_Should_Remove_And_Fire_Destroyed_When_Ephemeral()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Ephemeral()
            .Create();

        IDimension? destroyed = null;
        registry.Destroyed += (_, e) => destroyed = e.Dimension;

        Assert.True(registry.TryRemove(Code("a:b")));
        Assert.Null(registry.Get(Code("a:b")));
        Assert.NotNull(destroyed);
        Assert.Equal(dim.InternalId, destroyed!.InternalId);
    }

    [Fact]
    public void TryRemove_Should_Release_Id_For_Recycling()
    {
        var registry = NewRegistry();
        registry.DefineForOwner(Code("a:b"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();
        registry.TryRemove(Code("a:b"));
        var fresh = registry.DefineForOwner(Code("c:d"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();
        Assert.Equal(10, fresh.InternalId);
    }

    [Fact]
    public void SeedFromManifest_Should_Add_Pending_Entry()
    {
        var registry = NewRegistry();
        var entry = new ManifestEntry(
            Code("a:b"),
            42,
            DimensionLifetime.Persistent,
            "owner");
        registry.SeedFromManifest(entry, DimensionState.Pending);
        var dim = registry.Get(Code("a:b"));
        Assert.NotNull(dim);
        Assert.Equal(42, dim!.InternalId);
        Assert.Equal(DimensionState.Pending, dim.State);
    }

    [Fact]
    public void SeedFromManifest_Should_Add_Quarantined_Entry()
    {
        var registry = NewRegistry();
        var entry = new ManifestEntry(
            Code("ghost:b"),
            50,
            DimensionLifetime.Persistent,
            "ghost");
        registry.SeedFromManifest(entry, DimensionState.Quarantined);
        var dim = registry.Get(Code("ghost:b"));
        Assert.NotNull(dim);
        Assert.Equal(DimensionState.Quarantined, dim!.State);
    }

    [Fact]
    public void Define_RegisterStatic_Should_Promote_Pending_Entry_To_Active_And_Reuse_Id()
    {
        var registry = NewRegistry();
        var entry = new ManifestEntry(
            Code("testmod:nether"),
            42,
            DimensionLifetime.Persistent,
            "testmod");
        registry.SeedFromManifest(entry, DimensionState.Pending);

        var dim = registry.DefineForOwner(Code("testmod:nether"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        Assert.Equal(42, dim.InternalId);
        Assert.Equal(DimensionState.Active, dim.State);
    }

    [Fact]
    public void GetByInternalId_Should_Return_Dim_Or_Null()
    {
        var registry = NewRegistry();
        Assert.NotNull(registry.GetByInternalId(0));
        Assert.Null(registry.GetByInternalId(999));
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Same(dim, registry.GetByInternalId(dim.InternalId));
    }

    [Fact]
    public void DefineForOwner_RegisterStatic_Should_Propagate_GenerationRadius_To_DimensionImpl()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithGenerationRadius(7)
            .RegisterStatic();

        var impl = (Manifold.Internal.DimensionImpl)dim;
        Assert.Equal(7, impl.GenerationRadius);
    }

    [Fact]
    public void DefineForOwner_RegisterStatic_Should_Use_Default_GenerationRadius_When_Not_Configured()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var impl = (Manifold.Internal.DimensionImpl)dim;
        Assert.Equal(DimensionBuilderImpl.DefaultGenerationRadius, impl.GenerationRadius);
    }

    private static AssetLocation Code(string s) => new(s);

    private static DimensionRegistry NewRegistry()
    {
        var allocator = new DimensionAllocator();
        return new DimensionRegistry(allocator);
    }
}
