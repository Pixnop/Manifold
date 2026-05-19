using Manifold.Api.Helpers;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="BasicVoidWorldgenStrategy"/>.
/// </summary>
public sealed class BasicVoidWorldgenStrategyTests
{
    /// <summary>
    /// Verifies that OnInitialize does not throw.
    /// </summary>
    [Fact]
    public void OnInitialize_Should_Not_Throw()
    {
        var s = new BasicVoidWorldgenStrategy();
        s.OnInitialize(null!); // intentional null — the void strategy ignores its ctx
    }

    /// <summary>
    /// Verifies that GenerateColumn does not throw.
    /// </summary>
    [Fact]
    public void GenerateColumn_Should_Not_Throw()
    {
        var s = new BasicVoidWorldgenStrategy();
        s.GenerateColumn(null!); // intentional null — the void strategy ignores its ctx
    }
}
