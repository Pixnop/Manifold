using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>Server→client: a dimension has been removed.</summary>
[ProtoContract]
internal sealed class DimensionRemovedPacket
{
    /// <summary>Stringified code of the removed dimension.</summary>
    [ProtoMember(1)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Internal id of the removed dimension.</summary>
    [ProtoMember(2)]
    public int InternalId { get; set; }
}
