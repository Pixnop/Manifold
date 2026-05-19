using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class CurrentDimensionContextTests
{
    [Fact]
    public void Default_Should_Be_Zero()
    {
        Assert.Equal(0, CurrentDimensionContext.Current);
    }

    [Fact]
    public void Push_Should_Set_Then_Dispose_Should_Restore()
    {
        using (CurrentDimensionContext.Push(42))
        {
            Assert.Equal(42, CurrentDimensionContext.Current);
        }

        Assert.Equal(0, CurrentDimensionContext.Current);
    }

    [Fact]
    public void Push_Should_Nest()
    {
        using (CurrentDimensionContext.Push(42))
        {
            using (CurrentDimensionContext.Push(43))
            {
                Assert.Equal(43, CurrentDimensionContext.Current);
            }

            Assert.Equal(42, CurrentDimensionContext.Current);
        }

        Assert.Equal(0, CurrentDimensionContext.Current);
    }
}
