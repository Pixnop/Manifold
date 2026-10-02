using System.IO;
using Manifold.Internal.Networking;
using ProtoBuf;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class PlayerTransitedPacketTests
{
    [Fact]
    public void Yaw_Should_Round_Trip_Through_Protobuf()
    {
        var restored = RoundTrip(new PlayerTransitedPacket { SourceCode = "a:x", TargetCode = "a:y", TargetX = 1, Yaw = 2.5f });

        Assert.Equal(2.5f, restored.Yaw);
        Assert.Equal("a:y", restored.TargetCode);
        Assert.Equal(1, restored.TargetX);
    }

    [Fact]
    public void Yaw_Should_Round_Trip_Zero_As_Face_Yaw_Zero_Not_As_Keep_The_Yaw()
    {
        var restored = RoundTrip(new PlayerTransitedPacket { SourceCode = "a:x", TargetCode = "a:y", Yaw = 0f });

        Assert.Equal(0f, restored.Yaw);
    }

    [Fact]
    public void Yaw_Should_Stay_Null_When_The_Sender_Did_Not_Set_One()
    {
        // What a pre-0.6.1 server sends: no field 6 at all.
        Assert.Null(RoundTrip(new PlayerTransitedPacket { SourceCode = "a:x", TargetCode = "a:y" }).Yaw);
    }

    private static PlayerTransitedPacket RoundTrip(PlayerTransitedPacket packet)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, packet);
        ms.Position = 0;
        return Serializer.Deserialize<PlayerTransitedPacket>(ms);
    }
}
