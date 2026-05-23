using System;
using Manifold.Internal;
using NSubstitute;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class TransitMoversTests
{
    [Fact]
    public void Required_Should_Throw_When_Player_Is_Null()
    {
        var entity = Substitute.For<IEntityMover>();
        var movers = new TransitMovers(null!, entity);
        Assert.Throws<ArgumentNullException>(() => movers.Required());
    }

    [Fact]
    public void Required_Should_Throw_When_Entity_Is_Null()
    {
        var player = Substitute.For<IPlayerTeleporter>();
        var movers = new TransitMovers(player, null!);
        Assert.Throws<ArgumentNullException>(() => movers.Required());
    }

    [Fact]
    public void Required_Should_Return_Same_Instance_When_Both_Set()
    {
        var movers = new TransitMovers(Substitute.For<IPlayerTeleporter>(), Substitute.For<IEntityMover>());
        Assert.Same(movers, movers.Required());
    }
}
