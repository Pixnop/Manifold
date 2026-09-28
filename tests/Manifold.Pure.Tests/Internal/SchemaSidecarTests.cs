using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class SchemaSidecarTests
{
    [Fact]
    public void GetVersion_Should_Default_To_1_When_Sidecar_Is_Absent()
    {
        var sidecar = SchemaSidecar.Load(null);
        Assert.Equal(1, sidecar.GetVersion("manifold:genchunks"));
    }

    [Fact]
    public void GetVersion_Should_Default_To_1_For_A_Key_The_Sidecar_Has_No_Entry_For()
    {
        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:manifest", 1);

        Assert.Equal(1, sidecar.GetVersion("manifold:genchunks"));
    }

    [Fact]
    public void SetVersion_Then_ToBytes_LoadFromBytes_Should_Roundtrip()
    {
        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:manifest", 1);
        sidecar.SetVersion("manifold:genchunks", 3);

        var restored = SchemaSidecar.Load(sidecar.ToBytes());

        Assert.Equal(1, restored.GetVersion("manifold:manifest"));
        Assert.Equal(3, restored.GetVersion("manifold:genchunks"));
    }

    [Fact]
    public void Load_Should_Handle_Empty_And_Corrupt_Bytes()
    {
        Assert.Equal(1, SchemaSidecar.Load(System.Array.Empty<byte>()).GetVersion("k"));
        Assert.Equal(1, SchemaSidecar.Load(new byte[] { 0xFF, 0x01, 0x02 }).GetVersion("k"));
    }

    [Fact]
    public void RemoveVersion_Should_Make_The_Key_Read_Back_As_1()
    {
        var sidecar = SchemaSidecar.Load(null);
        sidecar.SetVersion("manifold:inv", 5);

        sidecar.RemoveVersion("manifold:inv");

        Assert.Equal(1, sidecar.GetVersion("manifold:inv"));
    }

    [Fact]
    public void IsEmpty_Should_Be_True_For_A_Fresh_Sidecar_And_False_Once_A_Version_Is_Set()
    {
        var sidecar = SchemaSidecar.Load(null);
        Assert.True(sidecar.IsEmpty);

        sidecar.SetVersion("manifold:inv", 1);
        Assert.False(sidecar.IsEmpty);

        sidecar.RemoveVersion("manifold:inv");
        Assert.True(sidecar.IsEmpty);
    }
}
