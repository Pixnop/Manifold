using ProtoBuf;

namespace Manifold.Internal.Networking;

/// <summary>
/// Discriminates the CLR type carried by a <see cref="MetadataEntry"/>. Mirrors exactly the set
/// <c>DimensionBuilderImpl.IsSupportedMetadataType</c> accepts, plus <see cref="Null"/> for a
/// <c>null</c> value.
/// </summary>
[ProtoContract]
internal enum MetadataValueKind
{
    /// <summary>Value is <c>null</c>.</summary>
    Null = 0,

    /// <summary><see cref="bool"/>.</summary>
    Boolean = 1,

    /// <summary><see cref="sbyte"/>.</summary>
    SByte = 2,

    /// <summary><see cref="byte"/>.</summary>
    Byte = 3,

    /// <summary><see cref="short"/>.</summary>
    Int16 = 4,

    /// <summary><see cref="ushort"/>.</summary>
    UInt16 = 5,

    /// <summary><see cref="int"/>.</summary>
    Int32 = 6,

    /// <summary><see cref="uint"/>.</summary>
    UInt32 = 7,

    /// <summary><see cref="long"/>.</summary>
    Int64 = 8,

    /// <summary><see cref="ulong"/>.</summary>
    UInt64 = 9,

    /// <summary><see cref="char"/>.</summary>
    Char = 10,

    /// <summary><see cref="float"/>.</summary>
    Single = 11,

    /// <summary><see cref="double"/>.</summary>
    Double = 12,

    /// <summary><see cref="nint"/>.</summary>
    IntPtr = 13,

    /// <summary><see cref="nuint"/>.</summary>
    UIntPtr = 14,

    /// <summary><see cref="string"/>.</summary>
    String = 15,

    /// <summary><see cref="byte"/>[].</summary>
    ByteArray = 16,

    /// <summary>Any <see cref="System.Enum"/> value.</summary>
    Enum = 17,
}
