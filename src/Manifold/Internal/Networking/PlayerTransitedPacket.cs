using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>Server→client (to the affected player only): transit completed.</summary>
[ProtoContract]
internal sealed class PlayerTransitedPacket
{
    /// <summary>Code of the dimension the player just left.</summary>
    [ProtoMember(1)]
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>Code of the dimension the player just entered.</summary>
    [ProtoMember(2)]
    public string TargetCode { get; set; } = string.Empty;

    /// <summary>X coordinate of the target position.</summary>
    [ProtoMember(3)]
    public int TargetX { get; set; }

    /// <summary>Y coordinate of the target position.</summary>
    [ProtoMember(4)]
    public int TargetY { get; set; }

    /// <summary>Z coordinate of the target position.</summary>
    [ProtoMember(5)]
    public int TargetZ { get; set; }

    /// <summary>
    /// The yaw, in radians, the player's camera must turn to, or <c>null</c> to leave it alone.
    /// Added in 0.6.1: an older client ignores the field, an older server never sets it.
    /// </summary>
    [ProtoMember(6)]
    public float? Yaw { get; set; }
}
