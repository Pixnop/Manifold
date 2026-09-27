using System;
using Manifold.Api.Events;
using Manifold.Internal;
using Manifold.Internal.Networking;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// Tests for <see cref="ManifoldClientFacade"/>.
/// </summary>
public sealed class ManifoldClientFacadeTests
{
    [Fact]
    public void Created_Should_Raise_With_The_Dimension_And_Facade_As_Sender()
    {
        var mirror = new ClientDimensionMirror();
        var facade = new ManifoldClientFacade(mirror);
        object? sender = null;
        Manifold.Api.Events.DimensionCreatedEventArgs? captured = null;
        facade.Created += (s, e) =>
        {
            sender = s;
            captured = e;
        };

        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = "a:b", InternalId = 10, OwnerModId = "owner" },
        });

        Assert.Same(facade, sender);
        Assert.NotNull(captured);
        Assert.Equal(new AssetLocation("a:b"), captured!.Dimension.Code);
        Assert.Equal(10, captured.Dimension.InternalId);
    }

    [Fact]
    public void Dimensions_And_Get_Should_Reflect_The_Mirror()
    {
        var mirror = new ClientDimensionMirror();
        var facade = new ManifoldClientFacade(mirror);

        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = "a:b", InternalId = 10, OwnerModId = "owner" },
        });

        Assert.Contains(facade.Dimensions, d => d.Code.Equals(new AssetLocation("a:b")));
        Assert.Equal(10, facade.Get(new AssetLocation("a:b"))!.InternalId);
        Assert.Null(facade.Get(new AssetLocation("nope:nope")));

        mirror.ApplyRemoved(new DimensionRemovedPacket { Code = "a:b", InternalId = 10 });

        Assert.DoesNotContain(facade.Dimensions, d => d.Code.Equals(new AssetLocation("a:b")));
        Assert.Null(facade.Get(new AssetLocation("a:b")));
    }

    [Fact]
    public void Created_Should_Isolate_A_Throwing_Subscriber()
    {
        var mirror = new ClientDimensionMirror();
        var facade = new ManifoldClientFacade(mirror);
        bool goodRan = false;
        facade.Created += (_, _) => throw new InvalidOperationException("rogue mod");
        facade.Created += (_, _) => goodRan = true;

        // A throwing third-party subscriber must not abort delivery to the others, same as the
        // server-side facade's events (SafeEvent).
        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = "a:b", InternalId = 10, OwnerModId = "owner" },
        });

        Assert.True(goodRan);
    }

    [Fact]
    public void Destroyed_Should_Isolate_A_Throwing_Subscriber()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = "a:b", InternalId = 10, OwnerModId = "owner" },
        });
        var facade = new ManifoldClientFacade(mirror);
        bool goodRan = false;
        facade.Destroyed += (_, _) => throw new InvalidOperationException("rogue mod");
        facade.Destroyed += (_, _) => goodRan = true;

        mirror.ApplyRemoved(new DimensionRemovedPacket { Code = "a:b", InternalId = 10 });

        Assert.True(goodRan);
    }

    [Fact]
    public void GetDimensionOf_Should_Return_The_Mirrored_Dimension_At_The_Entitys_Position()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = "a:b", InternalId = 10, OwnerModId = "owner" },
        });
        var facade = new ManifoldClientFacade(mirror);
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = 10;

        Assert.Equal(new AssetLocation("a:b"), facade.GetDimensionOf(entity)!.Code);
    }

    [Fact]
    public void GetDimensionOf_Should_Return_Null_When_The_Dimension_Is_Unknown_To_The_Mirror()
    {
        var facade = new ManifoldClientFacade(new ClientDimensionMirror());
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = 99;

        Assert.Null(facade.GetDimensionOf(entity));
    }

    [Fact]
    public void GetDimensionOf_Should_Throw_When_Entity_Is_Null()
    {
        var facade = new ManifoldClientFacade(new ClientDimensionMirror());

        Assert.Throws<ArgumentNullException>(() => facade.GetDimensionOf(null!));
    }

    [Fact]
    public void LocalPlayerChangedDimension_Should_Raise_With_The_Resolved_Args()
    {
        var mirror = new ClientDimensionMirror();
        var handler = new ClientTransitHandler(mirror, action => action());
        var facade = new ManifoldClientFacade(mirror, handler);
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = new DimensionDescriptor { Code = "a:overworld", InternalId = 0, OwnerModId = "manifold" } });
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = new DimensionDescriptor { Code = "mod:nether", InternalId = 10, OwnerModId = "mod" } });
        LocalPlayerDimensionChangedEventArgs? captured = null;
        facade.LocalPlayerChangedDimension += (_, e) => captured = e;

        handler.Handle(new PlayerTransitedPacket
        {
            SourceCode = "a:overworld",
            TargetCode = "mod:nether",
            TargetX = 1,
            TargetY = 2,
            TargetZ = 3,
        });

        Assert.NotNull(captured);
        Assert.Equal("mod:nether", captured!.Target.Code.ToString());
        Assert.Equal(1, captured.TargetPosition.X);
    }

    [Fact]
    public void LocalPlayerChangedDimension_Should_Isolate_A_Throwing_Subscriber()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = new DimensionDescriptor { Code = "a:overworld", InternalId = 0, OwnerModId = "manifold" } });
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = new DimensionDescriptor { Code = "mod:nether", InternalId = 10, OwnerModId = "mod" } });
        var handler = new ClientTransitHandler(mirror, action => action());
        var facade = new ManifoldClientFacade(mirror, handler);
        bool goodRan = false;
        facade.LocalPlayerChangedDimension += (_, _) => throw new InvalidOperationException("rogue mod");
        facade.LocalPlayerChangedDimension += (_, _) => goodRan = true;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "a:overworld", TargetCode = "mod:nether" });

        Assert.True(goodRan);
    }
}
