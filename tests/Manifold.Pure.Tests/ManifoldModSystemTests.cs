using System;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests;

/// <summary>
/// Tests for <see cref="ManifoldModSystem"/>'s pure helpers.
/// </summary>
public sealed class ManifoldModSystemTests
{
    [Fact]
    public void EvacuateOccupants_Should_Count_Only_Players_Rescue_Actually_Moved()
    {
        const int dimId = 42;
        var stuck = PlayerAt(dimId);
        var moved = PlayerAt(dimId);

        // "rescue" is best-effort: it succeeds for one occupant and silently fails for the other
        // (e.g. TeleportPlayer threw and RescueToOverworld swallowed it), same as production.
        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { stuck, moved },
            dimId,
            p =>
            {
                if (p == moved)
                {
                    p.Entity.Pos.Dimension = 0;
                }
            });

        Assert.Equal(1, evacuated);
        Assert.Equal(1, remaining);
    }

    [Fact]
    public void EvacuateOccupants_Should_Rescue_Every_Given_Player_Without_Re_Filtering()
    {
        // Callers now pass an already-filtered occupant list (e.g. OccupancyScan.PlayersIn), so
        // EvacuateOccupants itself must not re-check position before rescuing - it trusts the list.
        // A player whose recorded dimension does not match internalId still gets rescued here.
        var given = PlayerAt(7);
        bool rescued = false;

        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { given }, 42, _ => rescued = true);

        Assert.True(rescued);
    }

    [Fact]
    public void EvacuateOccupants_Should_Report_Remaining_When_No_One_Is_Evacuated()
    {
        const int dimId = 42;
        var stuck = PlayerAt(dimId);

        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { stuck }, dimId, _ => { /* best-effort rescue that does nothing */ });

        Assert.Equal(0, evacuated);
        Assert.Equal(1, remaining);
    }

    [Fact]
    public void SaveWorldState_Should_Not_Overwrite_A_Refused_Position_Store_Even_When_Dirty()
    {
        var manifestStore = new InMemoryManifestStore();
        var originalBytes = new byte[] { 1, 2, 3 };
        manifestStore.Write("manifold:lastpos", originalBytes);

        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:lastpos", 99);

        var positions = new PlayerPositionStore();
        positions.LoadFromBytes(originalBytes, version: 99); // refused: version 99 > SchemaVersion

        // Record() sets IsDirty regardless of refusal, same as a real transit reaching it in play.
        positions.Record("alice", 10, 1, 2, 3);

        var persistence = new DimensionPersistence(manifestStore, _ => true);
        var generatedColumns = new GeneratedColumnStore();

        ManifoldModSystem.SaveWorldState(
            manifestStore, persistence, Array.Empty<ManifestEntry>(), generatedColumns, positions, sidecar);

        Assert.Equal(originalBytes, manifestStore.Read("manifold:lastpos"));
        Assert.Equal(99, sidecar.GetVersion("manifold:lastpos"));
    }

    [Fact]
    public void SaveWorldState_Should_Persist_Origins_Under_Their_Own_Key_And_Version()
    {
        var manifestStore = new InMemoryManifestStore();
        var sidecar = SchemaSidecar.Load(null);
        var positions = new PlayerPositionStore();
        positions.Origins.Record("alice", 10, new OriginEntry(0, "manifold:overworld", 1.5, 2.5, 3.5, 0.5f));

        ManifoldModSystem.SaveWorldState(
            manifestStore, new DimensionPersistence(manifestStore, _ => true), Array.Empty<ManifestEntry>(), new GeneratedColumnStore(), positions, sidecar);

        var restored = new PlayerOriginStore();
        restored.LoadFromBytes(manifestStore.Read("manifold:origins"), version: sidecar.GetVersion("manifold:origins"));
        Assert.True(restored.TryGet("alice", 10, out var origin));
        Assert.Equal(new OriginEntry(0, "manifold:overworld", 1.5, 2.5, 3.5, 0.5f), origin);
        Assert.Equal(PlayerOriginStore.SchemaVersion, sidecar.GetVersion("manifold:origins"));
        Assert.False(positions.Origins.IsDirty);

        // The positions blob itself is untouched by the origins living next to it.
        Assert.Null(manifestStore.Read("manifold:lastpos"));
    }

    [Fact]
    public void SaveWorldState_Should_Not_Write_Origins_That_Did_Not_Change()
    {
        var manifestStore = new InMemoryManifestStore();

        ManifoldModSystem.SaveWorldState(
            manifestStore, new DimensionPersistence(manifestStore, _ => true), Array.Empty<ManifestEntry>(), new GeneratedColumnStore(), new PlayerPositionStore(), SchemaSidecar.Load(null));

        Assert.Null(manifestStore.Read("manifold:origins"));
    }

    [Fact]
    public void SaveWorldState_Should_Not_Overwrite_A_Refused_Origin_Store_Even_When_Dirty()
    {
        var manifestStore = new InMemoryManifestStore();
        var originalBytes = new byte[] { 1, 2, 3 };
        manifestStore.Write("manifold:origins", originalBytes);
        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:origins", 99);

        var positions = new PlayerPositionStore();
        positions.Origins.LoadFromBytes(originalBytes, version: 99); // refused
        positions.Origins.Record("alice", 10, new OriginEntry(0, "manifold:overworld", 1, 2, 3, 0f));

        ManifoldModSystem.SaveWorldState(
            manifestStore, new DimensionPersistence(manifestStore, _ => true), Array.Empty<ManifestEntry>(), new GeneratedColumnStore(), positions, sidecar);

        Assert.Equal(originalBytes, manifestStore.Read("manifold:origins"));
        Assert.Equal(99, sidecar.GetVersion("manifold:origins"));
    }

    [Fact]
    public void SaveWorldState_Should_Not_Advance_The_Manifest_Sidecar_Entry_When_The_Manifest_Is_Refused()
    {
        var manifestStore = new InMemoryManifestStore();
        manifestStore.Write(DimensionPersistence.ManifestKey, new byte[] { 9, 9 }); // stand-in newer blob

        var persistence = new DimensionPersistence(manifestStore, _ => true);
        _ = new System.Collections.Generic.List<ManifestEntry>(persistence.LoadOrEmpty(version: 99)); // latches IsVersionRefused
        Assert.True(persistence.IsVersionRefused);

        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion(DimensionPersistence.ManifestKey, 99);

        ManifoldModSystem.SaveWorldState(
            manifestStore, persistence, Array.Empty<ManifestEntry>(), new GeneratedColumnStore(), new PlayerPositionStore(), sidecar);

        Assert.Equal(99, sidecar.GetVersion(DimensionPersistence.ManifestKey));
    }

    [Fact]
    public void SaveWorldState_Should_Not_Write_A_Refused_GeneratedColumnStore_Even_If_Forced_Dirty()
    {
        var manifestStore = new InMemoryManifestStore();
        var originalBytes = new byte[] { 5, 6, 7, 8, 9, 10, 11, 12 }; // one packed long key
        manifestStore.Write("manifold:genchunks", originalBytes);

        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:genchunks", 99);

        var generatedColumns = new GeneratedColumnStore();
        generatedColumns.LoadFromBytes(originalBytes, version: 99); // refused
        generatedColumns.MarkGenerated(1, 2, 3); // force dirty despite the refusal

        var persistence = new DimensionPersistence(manifestStore, _ => true);

        ManifoldModSystem.SaveWorldState(
            manifestStore, persistence, Array.Empty<ManifestEntry>(), generatedColumns, new PlayerPositionStore(), sidecar);

        Assert.Equal(originalBytes, manifestStore.Read("manifold:genchunks"));
        Assert.Equal(99, sidecar.GetVersion("manifold:genchunks"));
    }

    // Entity.Pos is not a virtual member, so it cannot be stubbed; the substitute carries a real
    // EntityPos (created by the entity constructor) whose Dimension we set directly. EntityPosAccess
    // reads that same instance via reflection (see ManifoldServerFacadeTests.PlayerAt).
    private static IServerPlayer PlayerAt(int dimension)
    {
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = dimension;
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(entity);
        return player;
    }
}
