using System.Collections.Generic;
using Manifold.Api;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionPersistenceTests
{
    [Fact]
    public void Save_Should_Write_Manifest_Entry()
    {
        var store = new InMemoryManifestStore();
        var query = new FakeModLoaderQuery();
        var persistence = new DimensionPersistence(store, query);

        persistence.Save(new[]
        {
            new ManifestEntry(Code("mod:persistent"), 10, DimensionLifetime.Persistent, "mod"),
        });

        Assert.NotNull(store.Read(DimensionPersistence.ManifestKey));
    }

    [Fact]
    public void Save_Roundtrip_Should_Restore_Entries()
    {
        var store = new InMemoryManifestStore();
        var query = new FakeModLoaderQuery { LoadedMods = { "mod" } };
        var persistence = new DimensionPersistence(store, query);

        persistence.Save(new[]
        {
            new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod"),
            new ManifestEntry(Code("mod:b"), 11, DimensionLifetime.Persistent, "mod"),
        });

        var restored = new List<ManifestEntry>(persistence.LoadOrEmpty());
        Assert.Equal(2, restored.Count);
        Assert.Contains(restored, e => e.Code.Equals(Code("mod:a")) && e.InternalId == 10);
        Assert.Contains(restored, e => e.Code.Equals(Code("mod:b")) && e.InternalId == 11);
    }

    [Fact]
    public void Save_Should_Skip_Ephemeral_Entries()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, new FakeModLoaderQuery { LoadedMods = { "mod" } });

        persistence.Save(new[]
        {
            new ManifestEntry(Code("mod:p"), 10, DimensionLifetime.Persistent, "mod"),
            new ManifestEntry(Code("mod:e"), 11, DimensionLifetime.Ephemeral, "mod"),
        });

        var restored = new List<ManifestEntry>(persistence.LoadOrEmpty());
        Assert.Single(restored);
        Assert.Equal(Code("mod:p"), restored[0].Code);
    }

    [Fact]
    public void Save_Should_Skip_BuiltIn_Entries()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, new FakeModLoaderQuery());

        persistence.Save(new[]
        {
            new ManifestEntry(Code("manifold:overworld"), 0, DimensionLifetime.BuiltIn, "manifold"),
        });

        Assert.Empty(persistence.LoadOrEmpty());
    }

    [Fact]
    public void LoadOrEmpty_Should_Return_Empty_When_Store_Has_No_Data()
    {
        var persistence = new DimensionPersistence(new InMemoryManifestStore(), new FakeModLoaderQuery());
        Assert.Empty(persistence.LoadOrEmpty());
    }

    [Fact]
    public void LoadOrEmpty_Should_Return_Empty_When_Store_Has_Corrupted_Data()
    {
        var store = new InMemoryManifestStore();
        store.Write(DimensionPersistence.ManifestKey, new byte[] { 0x00, 0xFF, 0xAB });
        var persistence = new DimensionPersistence(store, new FakeModLoaderQuery());
        Assert.Empty(persistence.LoadOrEmpty());
    }

    [Fact]
    public void Classify_Should_Mark_Entry_Quarantined_When_Owner_Mod_Absent()
    {
        var persistence = new DimensionPersistence(new InMemoryManifestStore(), new FakeModLoaderQuery());
        var entry = new ManifestEntry(Code("ghost:dim"), 50, DimensionLifetime.Persistent, "ghost");
        Assert.Equal(DimensionState.Quarantined, persistence.Classify(entry));
    }

    [Fact]
    public void Classify_Should_Mark_Entry_Pending_When_Owner_Mod_Present()
    {
        var persistence = new DimensionPersistence(
            new InMemoryManifestStore(),
            new FakeModLoaderQuery { LoadedMods = { "mod" } });
        var entry = new ManifestEntry(Code("mod:dim"), 50, DimensionLifetime.Persistent, "mod");
        Assert.Equal(DimensionState.Pending, persistence.Classify(entry));
    }

    private static AssetLocation Code(string s) => new(s);
}
