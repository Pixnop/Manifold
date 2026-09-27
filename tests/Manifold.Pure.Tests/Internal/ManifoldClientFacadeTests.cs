using System;
using Manifold.Internal;
using Manifold.Internal.Networking;
using Vintagestory.API.Common;
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
}
