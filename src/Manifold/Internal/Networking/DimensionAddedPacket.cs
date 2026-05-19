using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>Server→client: a dimension has been activated.</summary>
[ProtoContract]
internal sealed class DimensionAddedPacket
{
    /// <summary>The added dimension descriptor.</summary>
    [ProtoMember(1)]
    public DimensionDescriptor Dimension { get; set; } = new();
}
