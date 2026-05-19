using Manifold.Api;
using Xunit;

namespace Manifold.Pure.Tests.ApiSurface;

public sealed class ExceptionContractTests
{
    [Fact]
    public void ManifoldException_Should_Inherit_From_System_Exception()
    {
        var ex = new TestException("msg");
        Assert.IsAssignableFrom<System.Exception>(ex);
        Assert.Equal("msg", ex.Message);
    }

    [Fact]
    public void All_Manifold_Exceptions_Should_Inherit_From_ManifoldException()
    {
        Assert.IsAssignableFrom<ManifoldException>(new ManifoldUnhealthyException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new ManifoldNotInitializedException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new DimensionAlreadyRegisteredException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new DimensionNotFoundException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new DimensionLifetimeUnspecifiedException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new DimensionCapacityExceededException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new DimensionStateException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new DimensionBuiltInImmutableException("x"));
        Assert.IsAssignableFrom<ManifoldException>(new WorldgenStrategyContractException("x"));
    }

    private sealed class TestException : ManifoldException
    {
        public TestException(string message)
            : base(message)
        {
        }
    }
}
