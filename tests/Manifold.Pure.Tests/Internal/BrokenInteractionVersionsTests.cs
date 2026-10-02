using Manifold.Internal.Util;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class BrokenInteractionVersionsTests
{
    [Theory]
    [InlineData("1.22.4")]
    [InlineData("1.22.5")]
    public void IsAffected_Should_Be_True_For_The_Two_Broken_Versions(string version)
    {
        Assert.True(BrokenInteractionVersions.IsAffected(version));
    }

    [Theory]
    [InlineData("1.22.3")]
    [InlineData("1.22.6")]
    [InlineData("1.22.7")]
    [InlineData("1.22.40")]
    [InlineData("1.23.4")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAffected_Should_Be_False_For_Any_Other_Version(string? version)
    {
        Assert.False(BrokenInteractionVersions.IsAffected(version));
    }

    [Fact]
    public void RunningGameVersion_Should_Read_The_Loaded_Game_Assembly()
    {
        string? running = BrokenInteractionVersions.RunningGameVersion();

        Assert.NotNull(running);
        Assert.Matches(@"^\d+\.\d+\.\d+", running);
    }
}
