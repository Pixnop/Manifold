using System;
using System.Collections.Generic;
using Manifold.Api;
using Vintagestory.API.Common;

namespace Manifold.Internal.Networking;

/// <summary>
/// Bidirectional conversion between <see cref="DimensionImpl"/> (server) and <see cref="DimensionDescriptor"/> (wire).
/// </summary>
internal static class DimensionDescriptorMapper
{
    /// <summary>Server → wire.</summary>
    /// <param name="dim">Server-side dimension.</param>
    /// <returns>Wire descriptor.</returns>
    public static DimensionDescriptor ToDescriptor(IDimension dim) => new()
    {
        Code = dim.Code.ToString(),
        InternalId = dim.InternalId,
        IsBuiltIn = dim.IsBuiltIn,
        Lifetime = (int)dim.Lifetime,
        OwnerModId = dim.OwnerModId,
        State = (int)dim.State,
        Metadata = ToMetadataEntries(dim.Metadata),
    };

    /// <summary>Wire → client-side <see cref="DimensionImpl"/> (Worldgen is always null on client).</summary>
    /// <param name="d">Wire descriptor.</param>
    /// <returns>Client-side dimension record.</returns>
    public static DimensionImpl ToImpl(DimensionDescriptor d) => DimensionImpl.Placeholder(
        new AssetLocation(d.Code),
        d.InternalId,
        d.IsBuiltIn,
        (DimensionLifetime)d.Lifetime,
        d.OwnerModId,
        (DimensionState)d.State) with
    {
        Metadata = FromMetadataEntries(d.Metadata),
    };

    /// <summary>Converts a dimension's metadata dictionary into its wire form.</summary>
    /// <param name="metadata">Server-side metadata.</param>
    /// <returns>One <see cref="MetadataEntry"/> per key.</returns>
    private static List<MetadataEntry> ToMetadataEntries(IReadOnlyDictionary<string, object?> metadata)
    {
        var list = new List<MetadataEntry>(metadata.Count);
        foreach (var kvp in metadata)
        {
            list.Add(ToMetadataEntry(kvp.Key, kvp.Value));
        }

        return list;
    }

    /// <summary>Converts a single metadata value into its wire form.</summary>
    /// <param name="key">Metadata key.</param>
    /// <param name="value">Metadata value; one of the types <c>DimensionBuilderImpl.IsSupportedMetadataType</c> accepts, or null.</param>
    /// <returns>The wire entry.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/> is of a type <c>WithMetadata</c> would already have rejected; unreachable in practice.
    /// </exception>
    private static MetadataEntry ToMetadataEntry(string key, object? value)
    {
        var entry = new MetadataEntry { Key = key };
        switch (value)
        {
            case null:
                entry.Kind = MetadataValueKind.Null;
                break;
            case bool b:
                entry.Kind = MetadataValueKind.Boolean;
                entry.IntegerValue = b ? 1 : 0;
                break;
            case sbyte sb:
                entry.Kind = MetadataValueKind.SByte;
                entry.IntegerValue = sb;
                break;
            case byte by:
                entry.Kind = MetadataValueKind.Byte;
                entry.IntegerValue = by;
                break;
            case short s16:
                entry.Kind = MetadataValueKind.Int16;
                entry.IntegerValue = s16;
                break;
            case ushort u16:
                entry.Kind = MetadataValueKind.UInt16;
                entry.IntegerValue = u16;
                break;
            case int i32:
                entry.Kind = MetadataValueKind.Int32;
                entry.IntegerValue = i32;
                break;
            case uint u32:
                entry.Kind = MetadataValueKind.UInt32;
                entry.IntegerValue = u32;
                break;
            case long i64:
                entry.Kind = MetadataValueKind.Int64;
                entry.IntegerValue = i64;
                break;
            case ulong u64:
                entry.Kind = MetadataValueKind.UInt64;
                entry.IntegerValue = unchecked((long)u64);
                break;
            case char c:
                entry.Kind = MetadataValueKind.Char;
                entry.IntegerValue = c;
                break;
            case float f32:
                entry.Kind = MetadataValueKind.Single;
                entry.FloatValue = f32;
                break;
            case double f64:
                entry.Kind = MetadataValueKind.Double;
                entry.FloatValue = f64;
                break;
            case IntPtr ip:
                entry.Kind = MetadataValueKind.IntPtr;
                entry.IntegerValue = (long)ip;
                break;
            case UIntPtr uip:
                entry.Kind = MetadataValueKind.UIntPtr;
                entry.IntegerValue = unchecked((long)(ulong)uip);
                break;
            case byte[] bytes:
                entry.Kind = MetadataValueKind.ByteArray;
                entry.BytesValue = bytes;
                break;
            case string str:
                entry.Kind = MetadataValueKind.String;
                entry.StringValue = str;
                break;
            case Enum e:
                entry.Kind = MetadataValueKind.Enum;
                var enumType = e.GetType();
                entry.EnumTypeFullName = enumType.FullName;
                entry.EnumAssemblyName = enumType.Assembly.GetName().Name;
                entry.IntegerValue = Convert.ToInt64(e);
                break;
            default:
                throw new NotSupportedException($"Unsupported metadata value type '{value.GetType()}'.");
        }

        return entry;
    }

    /// <summary>Converts wire metadata entries back into a dictionary for the client mirror.</summary>
    /// <param name="entries">Wire entries.</param>
    /// <returns>The reconstructed metadata dictionary, or the shared empty instance when there are none.</returns>
    private static IReadOnlyDictionary<string, object?> FromMetadataEntries(IReadOnlyList<MetadataEntry> entries)
    {
        if (entries.Count == 0)
        {
            return DimensionBuilderImpl.EmptyMetadata;
        }

        var dict = new Dictionary<string, object?>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            dict[entry.Key] = FromMetadataEntry(entry);
        }

        return dict;
    }

    /// <summary>Converts one wire entry back into its CLR value.</summary>
    /// <param name="entry">Wire entry.</param>
    /// <returns>The reconstructed value.</returns>
    /// <exception cref="NotSupportedException">The entry carries a <see cref="MetadataValueKind"/> this build does not know (a newer server sent a kind added later).</exception>
    private static object? FromMetadataEntry(MetadataEntry entry) => entry.Kind switch
    {
        MetadataValueKind.Null => null,
        MetadataValueKind.Boolean => entry.IntegerValue != 0,
        MetadataValueKind.SByte => (sbyte)entry.IntegerValue,
        MetadataValueKind.Byte => (byte)entry.IntegerValue,
        MetadataValueKind.Int16 => (short)entry.IntegerValue,
        MetadataValueKind.UInt16 => (ushort)entry.IntegerValue,
        MetadataValueKind.Int32 => (int)entry.IntegerValue,
        MetadataValueKind.UInt32 => (uint)entry.IntegerValue,
        MetadataValueKind.Int64 => entry.IntegerValue,
        MetadataValueKind.UInt64 => unchecked((ulong)entry.IntegerValue),
        MetadataValueKind.Char => (char)entry.IntegerValue,
        MetadataValueKind.Single => (float)entry.FloatValue,
        MetadataValueKind.Double => entry.FloatValue,
        MetadataValueKind.IntPtr => (IntPtr)entry.IntegerValue,
        MetadataValueKind.UIntPtr => unchecked((UIntPtr)(ulong)entry.IntegerValue),
        MetadataValueKind.String => entry.StringValue,
        MetadataValueKind.ByteArray => entry.BytesValue ?? Array.Empty<byte>(),
        MetadataValueKind.Enum => ResolveEnumValue(entry),
        _ => throw new NotSupportedException($"Unknown metadata value kind '{entry.Kind}'."),
    };

    /// <summary>
    /// Resolves an enum metadata entry back to its original <see cref="Enum"/> value when the
    /// owning type can be found client-side, else falls back to the raw underlying value.
    /// </summary>
    /// <param name="entry">Wire entry with <see cref="MetadataEntry.Kind"/> == <see cref="MetadataValueKind.Enum"/>.</param>
    /// <returns>The resolved enum instance, or <see cref="MetadataEntry.IntegerValue"/> boxed as <see cref="long"/> when the type cannot be resolved.</returns>
    private static object ResolveEnumValue(MetadataEntry entry)
    {
        var type = ResolveEnumType(entry.EnumTypeFullName, entry.EnumAssemblyName);
        return type is null ? entry.IntegerValue : Enum.ToObject(type, entry.IntegerValue);
    }

    /// <summary>
    /// Resolves an enum type by full name and declaring assembly's simple name. Tries
    /// <see cref="Type.GetType(string)"/> first, then falls back to scanning already-loaded
    /// assemblies: the game's mod loader can load a mod's assembly in a way <c>Type.GetType</c>
    /// does not see (its own load context), even though the assembly is present client-side.
    /// </summary>
    /// <param name="fullName">The enum type's <c>Type.FullName</c>.</param>
    /// <param name="assemblyName">The enum type's declaring assembly's simple name.</param>
    /// <returns>The resolved type, or <c>null</c> if it could not be found in any loaded assembly.</returns>
    private static Type? ResolveEnumType(string? fullName, string? assemblyName)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(assemblyName))
        {
            var direct = Type.GetType($"{fullName}, {assemblyName}", throwOnError: false);
            if (direct is not null)
            {
                return direct;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.IsNullOrEmpty(assemblyName)
                && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            var type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }
}
