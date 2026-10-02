using System;
using System.IO;
using System.Text;
using Manifold.Internal;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class PlayerOriginStoreTests
{
    private static readonly OriginEntry Sample = new(0, "manifold:overworld", 10.25, 64.5, -3.75, 1.5f);

    [Fact]
    public void TryGet_Should_Return_False_When_Nothing_Is_Recorded()
    {
        var store = new PlayerOriginStore();

        Assert.False(store.TryGet("alice", 10, out _));
    }

    [Fact]
    public void Record_Should_Make_The_Origin_Retrievable_And_Mark_The_Store_Dirty()
    {
        var store = new PlayerOriginStore();

        store.Record("alice", 10, Sample);

        Assert.True(store.IsDirty);
        Assert.True(store.TryGet("alice", 10, out var origin));
        Assert.Equal(Sample, origin);
    }

    [Fact]
    public void Record_Should_Replace_An_Earlier_Origin_For_The_Same_Player_And_Dimension()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);
        var newer = Sample with { SourceId = 11, SourceCode = "mod:b" };

        store.Record("alice", 10, newer);

        Assert.True(store.TryGet("alice", 10, out var origin));
        Assert.Equal(newer, origin);
    }

    [Fact]
    public void Record_Should_Keep_Players_And_Dimensions_Apart()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);

        Assert.False(store.TryGet("bob", 10, out _));
        Assert.False(store.TryGet("alice", 11, out _));
    }

    [Fact]
    public void ToBytes_Should_Round_Trip_Through_LoadFromBytes_Without_Losing_Precision()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);
        store.Record("bob", 11, new OriginEntry(10, "mod:a", 0.1, -0.2, 1e9 + 0.5, -2.5f));

        var restored = new PlayerOriginStore();
        restored.LoadFromBytes(store.ToBytes());

        Assert.True(restored.TryGet("alice", 10, out var alice));
        Assert.Equal(Sample, alice);
        Assert.True(restored.TryGet("bob", 11, out var bob));
        Assert.Equal(new OriginEntry(10, "mod:a", 0.1, -0.2, 1e9 + 0.5, -2.5f), bob);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public void ToBytes_Should_Match_The_Documented_Version_1_Layout()
    {
        // Golden bytes, hand-built so a change of layout cannot slip through by changing both sides:
        // count:int, then per entry key:string, sourceId:int, sourceCode:string, x,y,z:double, yaw:float.
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1);
            w.Write("alice|10");
            w.Write(0);
            w.Write("manifold:overworld");
            w.Write(10.25);
            w.Write(64.5);
            w.Write(-3.75);
            w.Write(1.5f);
        }

        Assert.Equal(ms.ToArray(), store.ToBytes());
    }

    [Fact]
    public void LoadFromBytes_Should_Start_Fresh_And_Warn_When_The_Data_Is_Corrupt()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);
        var logger = Substitute.For<ILogger>();

        store.LoadFromBytes(new byte[] { 0xFF, 0x01, 0x02 }, logger);

        Assert.False(store.TryGet("alice", 10, out _));
        logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void LoadFromBytes_Should_Read_Absent_Or_Empty_Data_As_An_Empty_Store()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);

        store.LoadFromBytes(null);
        Assert.False(store.TryGet("alice", 10, out _));
        Assert.False(store.IsVersionRefused);

        store.LoadFromBytes(Array.Empty<byte>());
        Assert.False(store.IsVersionRefused);
    }

    [Fact]
    public void LoadFromBytes_Should_Refuse_A_Schema_Version_Newer_Than_Supported()
    {
        var seed = new PlayerOriginStore();
        seed.Record("alice", 10, Sample);

        var store = new PlayerOriginStore();
        store.LoadFromBytes(seed.ToBytes(), version: PlayerOriginStore.SchemaVersion + 1);

        Assert.True(store.IsVersionRefused);
        Assert.False(store.TryGet("alice", 10, out _));
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void LoadFromBytes_Should_Not_Refuse_An_Unrecognized_Version_When_The_Blob_Is_Absent()
    {
        var store = new PlayerOriginStore();

        store.LoadFromBytes(null, version: 99);

        Assert.False(store.IsVersionRefused);
    }

    [Fact]
    public void LoadFromBytes_Should_Clear_IsVersionRefused_On_A_Later_Accepted_Load()
    {
        var seed = new PlayerOriginStore();
        seed.Record("alice", 10, Sample);
        var store = new PlayerOriginStore();
        store.LoadFromBytes(seed.ToBytes(), version: 99);
        Assert.True(store.IsVersionRefused);

        store.LoadFromBytes(seed.ToBytes());

        Assert.False(store.IsVersionRefused);
        Assert.True(store.TryGet("alice", 10, out _));
    }

    [Fact]
    public void ClearDirty_Should_Reset_The_Flag()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);

        store.ClearDirty();

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void RemoveDimension_Should_Drop_The_Origins_Recorded_For_It()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 10, Sample);
        store.Record("bob", 10, Sample);
        store.Record("alice", 12, Sample);
        store.ClearDirty();

        store.RemoveDimension(10);

        Assert.False(store.TryGet("alice", 10, out _));
        Assert.False(store.TryGet("bob", 10, out _));
        Assert.True(store.TryGet("alice", 12, out _));
        Assert.True(store.IsDirty);
    }

    [Fact]
    public void RemoveDimension_Should_Drop_The_Origins_That_Point_To_It()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 11, Sample with { SourceId = 10 });
        store.Record("bob", 12, Sample with { SourceId = 13 });
        store.ClearDirty();

        store.RemoveDimension(10);

        Assert.False(store.TryGet("alice", 11, out _));
        Assert.True(store.TryGet("bob", 12, out _));
        Assert.True(store.IsDirty);
    }

    [Fact]
    public void RemoveDimension_Should_Not_Match_A_Different_Dim_With_A_Shared_Suffix()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 110, Sample);

        store.RemoveDimension(10);

        Assert.True(store.TryGet("alice", 110, out _));
    }

    [Fact]
    public void RemoveDimension_Should_Leave_The_Store_Clean_When_Nothing_Matches()
    {
        var store = new PlayerOriginStore();
        store.Record("alice", 11, Sample);
        store.ClearDirty();

        store.RemoveDimension(99);

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void PlayerPositionStore_Should_Drop_Origins_Along_With_Positions_When_A_Dimension_Is_Removed()
    {
        var positions = new PlayerPositionStore();
        positions.Origins.Record("alice", 10, Sample);
        positions.Origins.Record("alice", 11, Sample with { SourceId = 10 });
        positions.Origins.ClearDirty();

        positions.RemoveDimension(10);

        Assert.False(positions.Origins.TryGet("alice", 10, out _));
        Assert.False(positions.Origins.TryGet("alice", 11, out _));
        Assert.True(positions.Origins.IsDirty);
    }

    [Fact]
    public void PlayerPositionStore_Should_Keep_Its_Own_Blob_Unchanged_By_Origins()
    {
        var plain = new PlayerPositionStore();
        plain.Record("alice", 10, 1, 2, 3);
        var withOrigins = new PlayerPositionStore();
        withOrigins.Record("alice", 10, 1, 2, 3);
        withOrigins.Origins.Record("alice", 10, Sample);

        Assert.Equal(plain.ToBytes(), withOrigins.ToBytes());
    }
}
