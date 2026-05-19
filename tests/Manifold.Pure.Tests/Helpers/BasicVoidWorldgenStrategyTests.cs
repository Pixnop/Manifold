using Manifold.Api.Helpers;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="BasicVoidWorldgenStrategy"/>.
/// </summary>
public sealed class BasicVoidWorldgenStrategyTests
{
    /// <summary>
    /// Verifies that the strategy declares only the Terrain pass.
    /// </summary>
    [Fact]
    public void Passes_Should_Contain_Terrain_Only()
    {
        var s = new BasicVoidWorldgenStrategy();
        Assert.Single(s.Passes);
        Assert.Contains(EnumWorldGenPass.Terrain, s.Passes);
    }

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
    /// Verifies that OnChunkColumnGen does not throw.
    /// </summary>
    [Fact]
    public void OnChunkColumnGen_Should_Not_Throw()
    {
        var s = new BasicVoidWorldgenStrategy();
        s.OnChunkColumnGen(null!, EnumWorldGenPass.Terrain);
    }
}
