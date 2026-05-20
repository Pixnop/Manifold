using Manifold.Api.Helpers;
using Manifold.Api.Worldgen;
using NSubstitute;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="BasicVoidWorldgenStrategy"/>.
/// </summary>
public sealed class BasicVoidWorldgenStrategyTests
{
    /// <summary>
    /// Verifies that OnInitialize does not throw and makes no block mutations.
    /// </summary>
    [Fact]
    public void OnInitialize_Should_Not_Throw()
    {
        var s = new BasicVoidWorldgenStrategy();
        var ctx = Substitute.For<IWorldgenInitContext>();
        var exception = Record.Exception(() => s.OnInitialize(ctx));
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that GenerateColumn does not throw and sets no blocks (void = all air).
    /// </summary>
    [Fact]
    public void GenerateColumn_Should_Not_Throw_And_Set_No_Blocks()
    {
        var s = new BasicVoidWorldgenStrategy();
        var ctx = Substitute.For<IWorldgenChunkContext>();
        var exception = Record.Exception(() => s.GenerateColumn(ctx));
        Assert.Null(exception);
        _ = ctx.DidNotReceiveWithAnyArgs().BlockAccessor;
    }
}
