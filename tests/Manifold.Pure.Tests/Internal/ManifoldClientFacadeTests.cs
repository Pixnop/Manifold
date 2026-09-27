using System;
using Manifold.Internal;
using Manifold.Internal.Networking;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// Tests for <see cref="ManifoldClientFacade"/>.
/// </summary>
public sealed class ManifoldClientFacadeTests
{
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
