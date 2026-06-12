using System;
using Manifold.Api;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class ManifoldServerFacadeTests
{
    [Fact]
    public void RelightRegion_Should_Throw_When_Dimension_Is_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<ArgumentNullException>(
            () => facade.RelightRegion(null!, new BlockPos(0, 0, 0, 0), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void RelightRegion_Should_Throw_When_Bounds_Are_Null()
    {
        var (facade, _) = NewFacade(healthy: true);
        var code = new AssetLocation("owner:target");
        Assert.Throws<ArgumentNullException>(
            () => facade.RelightRegion(code, null!, new BlockPos(1, 1, 1, 0)));
        Assert.Throws<ArgumentNullException>(
            () => facade.RelightRegion(code, new BlockPos(0, 0, 0, 0), null!));
    }

    [Fact]
    public void RelightRegion_Should_Throw_When_Unhealthy()
    {
        var (facade, _) = NewFacade(healthy: false);
        Assert.Throws<ManifoldUnhealthyException>(
            () => facade.RelightRegion(
                new AssetLocation("owner:target"), new BlockPos(0, 0, 0, 0), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void RelightRegion_Should_Throw_When_Dimension_Not_Found()
    {
        var (facade, _) = NewFacade(healthy: true);
        Assert.Throws<DimensionNotFoundException>(
            () => facade.RelightRegion(
                new AssetLocation("nope:nope"), new BlockPos(0, 0, 0, 0), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void RelightRegion_Should_Call_FullRelight_For_Known_Dimension()
    {
        var (facade, sapi) = NewFacade(healthy: true);

        facade.RelightRegion(
            new AssetLocation("owner:target"), new BlockPos(0, 0, 0, 0), new BlockPos(31, 64, 31, 0));

        sapi.WorldManager.Received(1).FullRelight(Arg.Any<BlockPos>(), Arg.Any<BlockPos>(), false);
    }

    private static (ManifoldServerFacade Facade, ICoreServerAPI Sapi) NewFacade(bool healthy)
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        registry.DefineForOwner(new AssetLocation("owner:target"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var sapi = Substitute.For<ICoreServerAPI>();
        var transitions = Substitute.For<Manifold.Api.Server.ITransitionService>();
        var facade = new ManifoldServerFacade(registry, transitions, sapi, healthy);
        return (facade, sapi);
    }
}
