using System;
using Manifold.Internal.Util;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class GuardsTests
{
    [Fact]
    public void NotNullOrWhiteSpace_Should_Throw_When_Null()
    {
        Assert.Throws<ArgumentNullException>(() => Guards.NotNullOrWhiteSpace(null!, "p"));
    }

    [Fact]
    public void NotNullOrWhiteSpace_Should_Throw_When_Whitespace()
    {
        Assert.Throws<ArgumentException>(() => Guards.NotNullOrWhiteSpace("   ", "p"));
    }

    [Fact]
    public void NotNullOrWhiteSpace_Should_Return_When_Valid()
    {
        Assert.Equal("ok", Guards.NotNullOrWhiteSpace("ok", "p"));
    }

    [Fact]
    public void InRange_Should_Throw_When_Below()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Guards.InRange(-1, 0, 10, "x"));
    }

    [Fact]
    public void InRange_Should_Throw_When_Above()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Guards.InRange(11, 0, 10, "x"));
    }

    [Fact]
    public void InRange_Should_Return_When_Inclusive()
    {
        Assert.Equal(0, Guards.InRange(0, 0, 10, "x"));
        Assert.Equal(10, Guards.InRange(10, 0, 10, "x"));
    }
}
