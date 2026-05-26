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
        var block = Substitute.For<IBlockMover>();
        var movers = new TransitMovers(null!, entity, block);
        Assert.Throws<ArgumentNullException>(() => movers.Required());
    }

    [Fact]
    public void Required_Should_Throw_When_Entity_Is_Null()
    {
        var player = Substitute.For<IPlayerTeleporter>();
        var block = Substitute.For<IBlockMover>();
        var movers = new TransitMovers(player, null!, block);
        Assert.Throws<ArgumentNullException>(() => movers.Required());
    }

    [Fact]
    public void Required_Should_Throw_When_Block_Is_Null()
    {
        var player = Substitute.For<IPlayerTeleporter>();
        var entity = Substitute.For<IEntityMover>();
        var movers = new TransitMovers(player, entity, null!);
        Assert.Throws<ArgumentNullException>(() => movers.Required());
    }

    [Fact]
    public void Required_Should_Return_Same_Instance_When_All_Set()
    {
        var movers = new TransitMovers(
            Substitute.For<IPlayerTeleporter>(),
            Substitute.For<IEntityMover>(),
            Substitute.For<IBlockMover>());
        Assert.Same(movers, movers.Required());
    }
}
