using Manifold.Api;
using Manifold.Internal;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionAllocatorTests
{
    [Fact]
    public void Reserve_Should_Return_First_FreeId_10_For_FirstAllocation()
    {
        var allocator = new DimensionAllocator();
        int id = allocator.Reserve(Code("mod:a"));
        Assert.Equal(10, id);
    }

    [Fact]
    public void Reserve_Should_Be_Idempotent_When_SameCode()
    {
        var allocator = new DimensionAllocator();
        int first = allocator.Reserve(Code("mod:a"));
        int second = allocator.Reserve(Code("mod:a"));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Reserve_Should_Hand_Different_Ids_To_Different_Codes()
    {
        var allocator = new DimensionAllocator();
        Assert.Equal(10, allocator.Reserve(Code("mod:a")));
        Assert.Equal(11, allocator.Reserve(Code("mod:b")));
        Assert.Equal(12, allocator.Reserve(Code("mod:c")));
    }

    [Fact]
    public void ReserveSpecific_Should_Lock_Requested_Id()
    {
        var allocator = new DimensionAllocator();
        allocator.ReserveSpecific(Code("mod:a"), 42);
        Assert.Equal(42, allocator.Reserve(Code("mod:a")));
    }

    [Fact]
    public void ReserveSpecific_Should_Skip_Locked_Id_On_Next_FreshReserve()
    {
        var allocator = new DimensionAllocator();
        allocator.ReserveSpecific(Code("mod:a"), 10);
        int next = allocator.Reserve(Code("mod:b"));
        Assert.NotEqual(10, next);
        Assert.Equal(11, next);
    }

    [Fact]
    public void ReserveSpecific_Should_Throw_When_Id_Out_Of_Range()
    {
        var allocator = new DimensionAllocator();
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            allocator.ReserveSpecific(Code("mod:a"), 5));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            allocator.ReserveSpecific(Code("mod:a"), 1024));
    }

    [Fact]
    public void ReserveSpecific_Should_Throw_When_Id_Already_Taken_By_Other_Code()
    {
        var allocator = new DimensionAllocator();
        allocator.ReserveSpecific(Code("mod:a"), 20);
        Assert.Throws<DimensionAlreadyRegisteredException>(() =>
            allocator.ReserveSpecific(Code("mod:b"), 20));
    }

    [Fact]
    public void ReserveSpecific_Should_Be_Idempotent_When_SameCodeAndId()
    {
        var allocator = new DimensionAllocator();
        allocator.ReserveSpecific(Code("mod:a"), 42);
        allocator.ReserveSpecific(Code("mod:a"), 42);
        Assert.Equal(42, allocator.Reserve(Code("mod:a")));
    }

    [Fact]
    public void Release_Should_Free_Id_For_Subsequent_Allocation()
    {
        var allocator = new DimensionAllocator();
        int a = allocator.Reserve(Code("mod:a"));   // 10
        int b = allocator.Reserve(Code("mod:b"));   // 11

        allocator.Release(a);

        int c = allocator.Reserve(Code("mod:c"));
        Assert.Equal(10, c);
        _ = b;
    }

    [Fact]
    public void Release_Should_Be_NoOp_When_Id_Not_Reserved()
    {
        var allocator = new DimensionAllocator();
        allocator.Release(50);
        Assert.Equal(10, allocator.Reserve(Code("mod:a")));
    }

    [Fact]
    public void Release_Should_Forget_Code_Mapping()
    {
        var allocator = new DimensionAllocator();
        int id = allocator.Reserve(Code("mod:a"));
        allocator.Release(id);
        int fresh = allocator.Reserve(Code("mod:a"));
        Assert.Equal(id, fresh);
    }

    [Fact]
    public void Reserve_Should_Throw_When_All_Mod_Ids_Are_Used()
    {
        var allocator = new DimensionAllocator();
        for (int i = 0; i < 1014; i++)
        {
            allocator.Reserve(Code($"mod:slot{i}"));
        }

        Assert.Throws<DimensionCapacityExceededException>(
            () => allocator.Reserve(Code("mod:overflow")));
    }

    [Fact]
    public void TryGetCode_Should_Return_True_And_Code_When_Id_Known()
    {
        var allocator = new DimensionAllocator();
        int id = allocator.Reserve(Code("mod:a"));
        Assert.True(allocator.TryGetCode(id, out var code));
        Assert.Equal("mod:a", code!.ToString());
    }

    [Fact]
    public void TryGetCode_Should_Return_False_When_Id_Unknown()
    {
        var allocator = new DimensionAllocator();
        Assert.False(allocator.TryGetCode(99, out var code));
        Assert.Null(code);
    }

    [Fact]
    public void TryGetId_Should_Return_True_And_Id_When_Code_Known()
    {
        var allocator = new DimensionAllocator();
        allocator.Reserve(Code("mod:a"));
        Assert.True(allocator.TryGetId(Code("mod:a"), out var id));
        Assert.Equal(10, id);
    }

    [Fact]
    public void TryGetId_Should_Return_False_When_Code_Unknown()
    {
        var allocator = new DimensionAllocator();
        Assert.False(allocator.TryGetId(Code("mod:nope"), out var id));
        Assert.Equal(0, id);
    }

    private static AssetLocation Code(string s) => new(s);
}
