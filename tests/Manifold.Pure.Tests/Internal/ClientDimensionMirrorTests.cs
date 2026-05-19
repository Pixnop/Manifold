using Manifold.Api;
using Manifold.Internal;
using Manifold.Internal.Networking;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class ClientDimensionMirrorTests
{
    [Fact]
    public void ApplyManifest_Should_Replace_Contents()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyManifest(new ManifestSnapshotPacket
        {
            Dimensions = { D("a:b", 10), D("c:d", 11) },
        });
        Assert.Equal(2, mirror.All.Count);
        Assert.NotNull(mirror.Get(new AssetLocation("a:b")));
    }

    [Fact]
    public void ApplyManifest_Should_Discard_Previous_Entries()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = D("first:a", 10) });
        mirror.ApplyManifest(new ManifestSnapshotPacket { Dimensions = { D("second:a", 11) } });
        Assert.Null(mirror.Get(new AssetLocation("first:a")));
        Assert.NotNull(mirror.Get(new AssetLocation("second:a")));
    }

    [Fact]
    public void ApplyAdded_Should_Add_New_Dimension()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = D("a:b", 10) });
        Assert.Single(mirror.All);
    }

    [Fact]
    public void ApplyRemoved_Should_Remove_Dimension()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = D("a:b", 10) });
        mirror.ApplyRemoved(new DimensionRemovedPacket { Code = "a:b", InternalId = 10 });
        Assert.Empty(mirror.All);
    }

    [Fact]
    public void ApplyRemoved_Should_NoOp_When_Unknown()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyRemoved(new DimensionRemovedPacket { Code = "nope:nope", InternalId = 99 });
        Assert.Empty(mirror.All);
    }

    [Fact]
    public void Added_Event_Should_Fire_On_ApplyAdded()
    {
        var mirror = new ClientDimensionMirror();
        IDimension? received = null;
        mirror.Added += d => received = d;
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = D("a:b", 10) });
        Assert.NotNull(received);
        Assert.Equal("a:b", received!.Code.ToString());
    }

    [Fact]
    public void Removed_Event_Should_Fire_On_ApplyRemoved()
    {
        var mirror = new ClientDimensionMirror();
        mirror.ApplyAdded(new DimensionAddedPacket { Dimension = D("a:b", 10) });
        IDimension? received = null;
        mirror.Removed += d => received = d;
        mirror.ApplyRemoved(new DimensionRemovedPacket { Code = "a:b", InternalId = 10 });
        Assert.NotNull(received);
        Assert.Equal(10, received!.InternalId);
    }

    private static DimensionDescriptor D(string code, int id) => new()
    {
        Code = code,
        InternalId = id,
        Lifetime = (int)DimensionLifetime.Persistent,
        OwnerModId = "owner",
        State = (int)DimensionState.Active,
    };
}
