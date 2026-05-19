using Manifold.Internal.Util;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionCodeValidatorTests
{
    [Fact]
    public void Validate_Should_Pass_When_Domain_And_Path_Are_Lowercase_Alphanumeric()
    {
        DimensionCodeValidator.Validate(Code("mymod:nether"));
        DimensionCodeValidator.Validate(Code("a:b"));
        DimensionCodeValidator.Validate(Code("mod123:dim_42"));
    }

    [Fact]
    public void Validate_Should_Throw_When_Code_Is_Null()
    {
        Assert.Throws<System.ArgumentNullException>(() => DimensionCodeValidator.Validate(null!));
    }

    [Fact]
    public void Validate_Should_Throw_When_Domain_Is_Reserved_Manifold_For_NonInternal_Caller()
    {
        var ex = Assert.Throws<System.ArgumentException>(
            () => DimensionCodeValidator.Validate(Code("manifold:nether")));
        Assert.Contains("manifold", ex.Message);
    }

    [Theory]
    [InlineData("my-mod:nether")]
    [InlineData("mymod:ne ther")]
    public void Validate_Should_Throw_When_Code_Format_Invalid(string raw)
    {
        Assert.Throws<System.ArgumentException>(() => DimensionCodeValidator.Validate(Code(raw)));
    }

    [Fact]
    public void ValidateInternal_Should_Allow_Manifold_Domain()
    {
        DimensionCodeValidator.ValidateInternal(Code("manifold:overworld"));
    }

    private static AssetLocation Code(string s) => new(s);
}
