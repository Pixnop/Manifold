using System.Collections.Generic;
using System.Linq;
using Manifold.Api;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionPersistenceTests
{
    [Fact]
    public void Save_Roundtrip_Should_Restore_Entries()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");

        var entries = new[]
        {
            new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod"),
            new ManifestEntry(Code("mod:b"), 11, DimensionLifetime.Persistent, "mod"),
        };
        persistence.Save(entries);

        // ManifestEntry is a record: this compares Code, InternalId, Lifetime and OwnerModId, so
        // dropping any one of them in Save would fail here (unlike a Code/InternalId-only check).
        Assert.Equal(entries, new List<ManifestEntry>(persistence.LoadOrEmpty()));
    }

    [Fact]
    public void Save_Should_Skip_Ephemeral_Entries()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");

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
        var persistence = new DimensionPersistence(store, _ => false);

        persistence.Save(new[]
        {
            new ManifestEntry(Code("manifold:overworld"), 0, DimensionLifetime.BuiltIn, "manifold"),
        });

        Assert.Empty(persistence.LoadOrEmpty());
    }

    [Fact]
    public void LoadOrEmpty_Should_Return_Empty_When_Store_Has_No_Data()
    {
        var persistence = new DimensionPersistence(new InMemoryManifestStore(), _ => false);
        Assert.Empty(persistence.LoadOrEmpty());
    }

    [Fact]
    public void LoadOrEmpty_Should_Return_Empty_When_Store_Has_Corrupted_Data()
    {
        var store = new InMemoryManifestStore();
        store.Write(DimensionPersistence.ManifestKey, new byte[] { 0x00, 0xFF, 0xAB });
        var persistence = new DimensionPersistence(store, _ => false);
        Assert.Empty(persistence.LoadOrEmpty());
    }

    [Fact]
    public void LoadOrEmpty_Should_Log_Error_When_Store_Has_Corrupted_Data()
    {
        var store = new InMemoryManifestStore();

        // Unlike { 0x00, 0xFF, 0xAB } above (parses to an empty tree with no throw), this byte
        // sequence makes TreeAttribute.FromBytes throw, exercising the actual catch block.
        store.Write(DimensionPersistence.ManifestKey, new byte[] { 0xFF, 0x01, 0x02 });
        var logger = Substitute.For<ILogger>();
        var persistence = new DimensionPersistence(store, _ => false, logger);

        // The corrupt manifest recovery (re-registration at boot) is otherwise silent; it must log.
        _ = new List<ManifestEntry>(persistence.LoadOrEmpty());

        logger.Received(1).Error(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void Save_Should_Match_The_0_5_1_Released_Format()
    {
        // Golden bytes: hand-built the same TreeAttribute shape Save is documented to write
        // ("entries" -> indexed children with code/id/lifetime/owner) independently of Save
        // itself, so a format change is caught even if the writer and this assertion drifted
        // together. Unchanged since v0.5.1 (git show v0.5.1:src/Manifold/Internal/DimensionPersistence.cs).
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");

        persistence.Save(new[] { new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod") });

        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        var list = new Vintagestory.API.Datastructures.TreeAttribute();
        var child = new Vintagestory.API.Datastructures.TreeAttribute();
        child.SetString("code", "mod:a");
        child.SetInt("id", 10);
        child.SetInt("lifetime", (int)DimensionLifetime.Persistent);
        child.SetString("owner", "mod");
        list["0"] = (Vintagestory.API.Datastructures.IAttribute)child;
        tree["entries"] = (Vintagestory.API.Datastructures.IAttribute)list;

        Assert.Equal(tree.ToBytes(), store.Read(DimensionPersistence.ManifestKey));
    }

    [Fact]
    public void LoadOrEmpty_Should_Read_A_Blob_With_No_Sidecar_Entry_As_Version_1()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");
        var entry = new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod");
        persistence.Save(new[] { entry });

        Assert.Equal(new[] { entry }, new List<ManifestEntry>(persistence.LoadOrEmpty())); // version defaults to 1
        Assert.False(persistence.IsVersionRefused);
    }

    [Fact]
    public void LoadOrEmpty_Should_Refuse_A_Schema_Version_Newer_Than_Supported()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");

        // A payload that IS valid for the current parser (real tree bytes, not garbage) proves
        // refusal is driven by the version check, not by the corrupt-data catch block.
        persistence.Save(new[] { new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod") });

        Assert.Empty(persistence.LoadOrEmpty(version: 99));
        Assert.True(persistence.IsVersionRefused);
    }

    [Fact]
    public void LoadOrEmpty_Should_Preserve_The_Raw_Blob_Under_An_Unrecognized_Key()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");
        persistence.Save(new[] { new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod") });
        var raw = store.Read(DimensionPersistence.ManifestKey);

        _ = new List<ManifestEntry>(persistence.LoadOrEmpty(version: 99));

        Assert.Equal(raw, store.Read(DimensionPersistence.UnrecognizedManifestKey));
    }

    [Fact]
    public void LoadOrEmpty_Should_Log_Error_Naming_The_Key_And_Both_Versions_For_An_Unrecognized_Future_Schema_Version()
    {
        var store = new InMemoryManifestStore();
        var logger = Substitute.For<ILogger>();
        var persistence = new DimensionPersistence(store, m => m == "mod", logger);
        persistence.Save(new[] { new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod") });

        _ = new List<ManifestEntry>(persistence.LoadOrEmpty(version: 99));

        logger.Received(1).Error(
            Arg.Any<string>(),
            Arg.Is<object[]>(a => a.Contains(DimensionPersistence.ManifestKey) && a.Contains(99) && a.Contains(DimensionPersistence.SchemaVersion)));
    }

    [Fact]
    public void Save_Should_Be_A_NoOp_When_The_Last_Load_Refused_The_Schema_Version()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");
        persistence.Save(new[] { new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod") });
        var raw = store.Read(DimensionPersistence.ManifestKey);
        _ = new List<ManifestEntry>(persistence.LoadOrEmpty(version: 99));

        // The registry has not finished re-registering everything yet this session; writing now
        // would overwrite the newer manifest (preserved above) with an incomplete one.
        persistence.Save(new[] { new ManifestEntry(Code("mod:b"), 20, DimensionLifetime.Persistent, "mod") });

        Assert.Equal(raw, store.Read(DimensionPersistence.ManifestKey));
    }

    [Fact]
    public void LoadOrEmpty_Should_Clear_IsVersionRefused_On_A_Later_Accepted_Load()
    {
        var store = new InMemoryManifestStore();
        var persistence = new DimensionPersistence(store, m => m == "mod");
        persistence.Save(new[] { new ManifestEntry(Code("mod:a"), 10, DimensionLifetime.Persistent, "mod") });
        _ = new List<ManifestEntry>(persistence.LoadOrEmpty(version: 99));
        Assert.True(persistence.IsVersionRefused);

        _ = new List<ManifestEntry>(persistence.LoadOrEmpty());

        Assert.False(persistence.IsVersionRefused);
    }

    [Fact]
    public void Classify_Should_Mark_Entry_Quarantined_When_Owner_Mod_Absent()
    {
        var persistence = new DimensionPersistence(new InMemoryManifestStore(), _ => false);
        var entry = new ManifestEntry(Code("ghost:dim"), 50, DimensionLifetime.Persistent, "ghost");
        Assert.Equal(DimensionState.Quarantined, persistence.Classify(entry));
    }

    [Fact]
    public void Classify_Should_Mark_Entry_Pending_When_Owner_Mod_Present()
    {
        var persistence = new DimensionPersistence(
            new InMemoryManifestStore(),
            m => m == "mod");
        var entry = new ManifestEntry(Code("mod:dim"), 50, DimensionLifetime.Persistent, "mod");
        Assert.Equal(DimensionState.Pending, persistence.Classify(entry));
    }

    private static AssetLocation Code(string s) => new(s);
}
