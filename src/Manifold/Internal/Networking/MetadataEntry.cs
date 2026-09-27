using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>Wire representation of a single <see cref="Manifold.Api.IDimension.Metadata"/> entry.</summary>
/// <remarks>
/// A hand-rolled discriminated union rather than a protobuf map, because <c>WithMetadata</c>
/// accepts several unrelated CLR types (see <see cref="MetadataValueKind"/>). Every integer-like
/// kind (including <c>bool</c>, <c>char</c> and an enum's underlying value) is carried in
/// <see cref="IntegerValue"/>; floating-point kinds use <see cref="FloatValue"/>.
/// </remarks>
[ProtoContract]
internal sealed class MetadataEntry
{
    /// <summary>Metadata key.</summary>
    [ProtoMember(1)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Which field(s) below hold the value.</summary>
    [ProtoMember(2)]
    public MetadataValueKind Kind { get; set; }

    /// <summary>
    /// Value for every integer-like <see cref="Kind"/>: <c>bool</c> as 0/1, <c>char</c> as its code
    /// point, every integral primitive up to 64 bits, and an enum's underlying value converted with
    /// <c>Convert.ToInt64</c> (throws for a <c>ulong</c>-backed enum above <see cref="long.MaxValue"/>,
    /// the one case this format cannot carry).
    /// </summary>
    [ProtoMember(3)]
    public long IntegerValue { get; set; }

    /// <summary>Value for <see cref="MetadataValueKind.Single"/>/<see cref="MetadataValueKind.Double"/>.</summary>
    [ProtoMember(4)]
    public double FloatValue { get; set; }

    /// <summary>Value for <see cref="MetadataValueKind.String"/>.</summary>
    [ProtoMember(5)]
    public string? StringValue { get; set; }

    /// <summary>Value for <see cref="MetadataValueKind.ByteArray"/>.</summary>
    [ProtoMember(6)]
    public byte[]? BytesValue { get; set; }

    /// <summary>For <see cref="MetadataValueKind.Enum"/>: the enum type's <c>Type.FullName</c>.</summary>
    [ProtoMember(7)]
    public string? EnumTypeFullName { get; set; }

    /// <summary>For <see cref="MetadataValueKind.Enum"/>: the enum type's declaring assembly's simple name.</summary>
    [ProtoMember(8)]
    public string? EnumAssemblyName { get; set; }
}
