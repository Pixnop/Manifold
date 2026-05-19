using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>Wire representation of an <see cref="Manifold.Api.IDimension"/> snapshot for client mirroring.</summary>
[ProtoContract]
internal sealed class DimensionDescriptor
{
    /// <summary>Stringified <c>AssetLocation</c> (domain:path).</summary>
    [ProtoMember(1)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Engine dimension id.</summary>
    [ProtoMember(2)]
    public int InternalId { get; set; }

    /// <summary>True if this is the built-in overworld.</summary>
    [ProtoMember(3)]
    public bool IsBuiltIn { get; set; }

    /// <summary>Lifetime as int (cast from <see cref="Manifold.Api.DimensionLifetime"/>).</summary>
    [ProtoMember(4)]
    public int Lifetime { get; set; }

    /// <summary>Owning mod id.</summary>
    [ProtoMember(5)]
    public string OwnerModId { get; set; } = string.Empty;

    /// <summary>State as int (cast from <see cref="Manifold.Api.DimensionState"/>).</summary>
    [ProtoMember(6)]
    public int State { get; set; }
}
