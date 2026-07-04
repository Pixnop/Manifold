using System;
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

    [Fact]
    public void WithDarkSky_Should_Set_SkyCapY_And_Bump_RelightHeight()
    {
        var registry = NewRegistry();
        var dim = (Manifold.Internal.DimensionImpl)registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithDarkSky(ceilingY: 30)
            .RegisterStatic();

        Assert.Equal(30, dim.SkyCapY);

        // Relight band must cover the cap (capY + 1) or the cap never takes effect.
        Assert.Equal(31, dim.RelightHeight);
    }

    [Fact]
    public void WithDarkSky_Should_Not_Lower_An_Already_Higher_RelightHeight()
    {
        var registry = NewRegistry();
        var dim = (Manifold.Internal.DimensionImpl)registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithRelightHeight(200)
            .WithDarkSky(ceilingY: 30)
            .RegisterStatic();

        Assert.Equal(30, dim.SkyCapY);
        Assert.Equal(200, dim.RelightHeight);
    }

    [Fact]
    public void Dimension_Without_DarkSky_Should_Have_Null_SkyCapY()
    {
        var registry = NewRegistry();
        var dim = (Manifold.Internal.DimensionImpl)registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        Assert.Null(dim.SkyCapY);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1025)]
    public void WithDarkSky_Should_Reject_Out_Of_Range(int ceilingY)
    {
        var registry = NewRegistry();
        var builder = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy());

        Assert.ThrowsAny<ArgumentException>(() => builder.WithDarkSky(ceilingY));
    }

    [Fact]
    public void WithMetadata_Should_Attach_Values_To_Dimension()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithMetadata("display_name", "Test Dim")
            .WithMetadata("level", 5)
            .WithMetadata("hub_visible", true)
            .RegisterStatic();

        Assert.Equal("Test Dim", dim.GetMetadata<string>("display_name"));
        Assert.Equal(5, dim.GetMetadata<int>("level"));
        Assert.True(dim.GetMetadata<bool>("hub_visible"));
        Assert.True(dim.HasMetadata("display_name"));
    }

    [Fact]
    public void Metadata_Should_Not_Be_Downcastable_To_A_Mutable_Dictionary()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithMetadata("k", "v")
            .RegisterStatic();

        // The published read-only map must be immutable so a consumer cannot downcast it back to a
        // Dictionary and mutate the registry's snapshot value out-of-band.
        Assert.False(dim.Metadata is System.Collections.Generic.Dictionary<string, object?>);
    }

    [Fact]
    public void Dimension_Without_Metadata_Should_Expose_Empty_Map()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        Assert.Empty(dim.Metadata);
        Assert.False(dim.HasMetadata("anything"));
        Assert.Null(dim.GetMetadata<string>("missing"));
        Assert.Equal("default", dim.GetMetadata("missing", "default"));
    }

    [Fact]
    public void Overworld_Should_Have_Empty_Metadata()
    {
        var registry = NewRegistry();
        var overworld = registry.Get(new AssetLocation("manifold:overworld"));
        Assert.NotNull(overworld);
        Assert.Empty(overworld!.Metadata);
    }

    [Fact]
    public void WithMetadata_Should_Reject_Unsupported_Value_Type()
    {
        var registry = NewRegistry();
        var builder = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy());

        Assert.Throws<ArgumentException>(() => builder.WithMetadata("bad", new System.Collections.Generic.List<int> { 1 }));
    }

    [Fact]
    public void WithMetadata_Should_Reject_Duplicate_Key()
    {
        var registry = NewRegistry();
        var builder = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithMetadata("name", "first");

        Assert.Throws<ArgumentException>(() => builder.WithMetadata("name", "second"));
    }

    [Fact]
    public void WithMetadata_Should_Reject_Empty_Key()
    {
        var registry = NewRegistry();
        var builder = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy());

        Assert.ThrowsAny<ArgumentException>(() => builder.WithMetadata(string.Empty, "value"));
    }

    [Fact]
    public void WithMetadata_Should_Accept_Null_Value()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithMetadata("absent", null)
            .RegisterStatic();

        Assert.True(dim.HasMetadata("absent"));
        Assert.Null(dim.GetMetadata<string>("absent"));
    }

    [Fact]
    public void GetMetadata_Should_Return_Default_When_Wrong_Type()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .WithMetadata("count", 7)
            .RegisterStatic();

        Assert.Equal("fallback", dim.GetMetadata("count", "fallback"));
    }

    [Fact]
    public void WithStreamingBudget_Should_Propagate_To_DimensionImpl()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .Streaming(loadRadius: 4)
            .WithStreamingBudget(maxColumnsPerTick: 8)
            .RegisterStatic();

        var impl = (Manifold.Internal.DimensionImpl)dim;
        Assert.Equal(8, impl.StreamingBudgetPerTick);
    }

    [Fact]
    public void Dimension_Without_StreamingBudget_Should_Be_Null()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var impl = (Manifold.Internal.DimensionImpl)dim;
        Assert.Null(impl.StreamingBudgetPerTick);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65)]
    public void WithStreamingBudget_Should_Reject_Out_Of_Range(int value)
    {
        var registry = NewRegistry();
        var builder = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy());

        Assert.ThrowsAny<ArgumentException>(() => builder.WithStreamingBudget(value));
    }

    [Fact]
    public void TryRemove_Should_Refuse_When_Dimension_Is_Occupied()
    {
        // Occupancy predicate reports the (first-allocated, id 10) dimension as occupied.
        var registry = NewRegistry(internalId => internalId == 10);
        registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();

        bool destroyedFired = false;
        registry.Destroyed += (_, _) => destroyedFired = true;

        Assert.False(registry.TryRemove(Code("a:b")));
        Assert.NotNull(registry.Get(Code("a:b")));
        Assert.False(destroyedFired);
    }

    [Fact]
    public void TryRemove_Should_Succeed_When_Occupancy_Predicate_Reports_Empty()
    {
        var registry = NewRegistry(_ => false);
        registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();

        Assert.True(registry.TryRemove(Code("a:b")));
        Assert.Null(registry.Get(Code("a:b")));
    }

    [Fact]
    public void TryRemove_Occupancy_Guard_Does_Not_Run_For_Persistent_Dimensions()
    {
        // Persistent removal still throws before any occupancy check (immutability wins).
        var registry = NewRegistry(_ => false);
        registry.DefineForOwner(Code("a:b"), "testmod").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Throws<DimensionStateException>(() => registry.TryRemove(Code("a:b")));
    }

    [Fact]
    public void DefineForOwner_Should_Reject_Pending_Dimension_Owned_By_Another_Mod()
    {
        var registry = NewRegistry();
        registry.SeedFromManifest(
            new ManifestEntry(Code("owner_a:dim"), 42, DimensionLifetime.Persistent, "owner_a"),
            DimensionState.Pending);

        Assert.Throws<DimensionAlreadyRegisteredException>(
            () => registry.DefineForOwner(Code("owner_a:dim"), "owner_b"));
    }

    [Fact]
    public void DefineForOwner_Should_Let_Rightful_Owner_Complete_Pending_Dimension()
    {
        var registry = NewRegistry();
        registry.SeedFromManifest(
            new ManifestEntry(Code("owner_a:dim"), 42, DimensionLifetime.Persistent, "owner_a"),
            DimensionState.Pending);

        var dim = registry.DefineForOwner(Code("owner_a:dim"), "owner_a")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        Assert.Equal(DimensionState.Active, dim.State);
        Assert.Equal("owner_a", dim.OwnerModId);
    }

    [Fact]
    public void Purge_Should_Remove_Persistent_Dimension_And_Release_Id()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.True(registry.Purge(Code("a:b")));
        Assert.Null(registry.Get(Code("a:b")));

        // Released id must re-enter the allocator pool (the leak the gap was about).
        var fresh = registry.DefineForOwner(Code("c:d"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Equal(dim.InternalId, fresh.InternalId);
    }

    [Fact]
    public void Purge_Should_Remove_Quarantined_Dimension()
    {
        var registry = NewRegistry();
        registry.SeedFromManifest(
            new ManifestEntry(Code("ghost:dim"), 42, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);

        Assert.True(registry.Purge(Code("ghost:dim")));
        Assert.Null(registry.Get(Code("ghost:dim")));
    }

    [Fact]
    public void Purge_Should_Fire_Destroyed()
    {
        var registry = NewRegistry();
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        IDimension? destroyed = null;
        registry.Destroyed += (_, e) => destroyed = e.Dimension;

        registry.Purge(Code("a:b"));

        Assert.NotNull(destroyed);
        Assert.Equal(dim.InternalId, destroyed!.InternalId);
    }

    [Fact]
    public void Purge_Should_Return_False_When_Code_Unknown()
    {
        var registry = NewRegistry();
        Assert.False(registry.Purge(Code("nope:nope")));
    }

    [Fact]
    public void Purge_Should_Throw_For_BuiltIn()
    {
        var registry = NewRegistry();
        Assert.Throws<DimensionBuiltInImmutableException>(() => registry.Purge(Code("manifold:overworld")));
    }

    [Fact]
    public void Created_Should_Isolate_A_Throwing_Subscriber()
    {
        var registry = NewRegistry();
        bool goodRan = false;
        registry.Created += (_, _) => throw new InvalidOperationException("rogue subscriber");
        registry.Created += (_, _) => goodRan = true;

        // A throwing third-party subscriber must not abort the registering mod's call.
        var dim = registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        Assert.NotNull(dim);
        Assert.Same(dim, registry.Get(Code("a:b")));
        Assert.True(goodRan);
    }

    [Fact]
    public void Destroyed_Should_Isolate_A_Throwing_Subscriber()
    {
        var registry = NewRegistry(_ => false);
        registry.DefineForOwner(Code("a:b"), "testmod")
            .WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();

        bool goodRan = false;
        registry.Destroyed += (_, _) => throw new InvalidOperationException("rogue subscriber");
        registry.Destroyed += (_, _) => goodRan = true;

        Assert.True(registry.TryRemove(Code("a:b")));
        Assert.Null(registry.Get(Code("a:b")));
        Assert.True(goodRan);
    }

    private static AssetLocation Code(string s) => new(s);

    private static DimensionRegistry NewRegistry()
    {
        var allocator = new DimensionAllocator();
        return new DimensionRegistry(allocator);
    }

    private static DimensionRegistry NewRegistry(System.Func<int, bool> isOccupied)
    {
        var allocator = new DimensionAllocator();
        return new DimensionRegistry(allocator, isOccupied);
    }
}
