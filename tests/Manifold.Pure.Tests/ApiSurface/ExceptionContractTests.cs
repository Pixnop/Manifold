using System;
using System.Linq;
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
        // Reflection over every *Exception type in the API, not a hand-picked list: a future
        // exception that forgets to derive from ManifoldException is caught automatically.
        var exceptionTypes = typeof(ManifoldException).Assembly.GetExportedTypes()
            .Where(t => t.Name.EndsWith("Exception", StringComparison.Ordinal) && t != typeof(ManifoldException));

        Assert.All(exceptionTypes, t => Assert.True(typeof(ManifoldException).IsAssignableFrom(t), t.Name));
    }

    private sealed class TestException : ManifoldException
    {
        public TestException(string message)
            : base(message)
        {
        }
    }
}
