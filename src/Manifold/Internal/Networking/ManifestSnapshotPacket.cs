using System.Collections.Generic;
using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>Server→client (on join): full manifest snapshot.</summary>
[ProtoContract]
internal sealed class ManifestSnapshotPacket
{
    /// <summary>All dimensions known to the server at snapshot time.</summary>
    [ProtoMember(1)]
    public List<DimensionDescriptor> Dimensions { get; set; } = new();
}
