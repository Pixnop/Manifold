using Manifold.Api.Helpers;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="DimensionCommandBuilder"/>.
/// </summary>
public sealed class DimensionCommandBuilderTests
{
    /// <summary>
    /// Verifies that the builder defaults the privilege to "chat".
    /// </summary>
    [Fact]
    public void Builder_Should_Default_Privilege_To_Chat()
    {
        var b = new DimensionCommandBuilder();
        Assert.Equal("chat", b.Privilege);
    }

    /// <summary>
    /// Verifies that Validate throws when TargetDimension is not set.
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_TargetDimension_Missing()
    {
        var b = new DimensionCommandBuilder().Command("voiddim");
        Assert.Throws<System.InvalidOperationException>(() => b.Validate());
    }

    /// <summary>
    /// Verifies that Validate throws when Command name is not set.
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_Name_Missing()
    {
        var b = new DimensionCommandBuilder().TargetDimension(new AssetLocation("mod:dim"));
        Assert.Throws<System.InvalidOperationException>(() => b.Validate());
    }

    /// <summary>
    /// Verifies that Validate passes when both required fields are set.
    /// </summary>
    [Fact]
    public void Validate_Should_Pass_When_Name_And_Target_Set()
    {
        new DimensionCommandBuilder()
            .Command("voiddim")
            .TargetDimension(new AssetLocation("mod:dim"))
            .Validate();
    }

    /// <summary>
    /// Verifies that builder methods are chainable.
    /// </summary>
    [Fact]
    public void Builder_Methods_Should_Be_Chainable()
    {
        var b = new DimensionCommandBuilder()
            .Command("test")
            .TargetDimension(new AssetLocation("mod:t"))
            .RequiresPrivilege("controlserver")
            .DescribedAs("Custom desc");
        Assert.Equal("test", b.Name);
        Assert.Equal("controlserver", b.Privilege);
        Assert.Equal("Custom desc", b.Description);
    }
}
