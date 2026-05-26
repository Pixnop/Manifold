using System;

namespace Manifold.Api;

/// <summary>Typed accessors over <see cref="IDimension.Metadata"/>.</summary>
public static class DimensionMetadataExtensions
{
    /// <summary>
    /// Returns the metadata value for <paramref name="key"/> cast to <typeparamref name="T"/>,
    /// or <paramref name="defaultValue"/> if the key is absent or the stored value is not a
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Expected value type.</typeparam>
    /// <param name="dimension">Dimension to read from.</param>
    /// <param name="key">Metadata key.</param>
    /// <param name="defaultValue">Returned when the key is missing or has the wrong type.</param>
    /// <returns>The typed metadata value, or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dimension"/> or <paramref name="key"/> is null.</exception>
    public static T? GetMetadata<T>(this IDimension dimension, string key, T? defaultValue = default)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(key);

        if (dimension.Metadata.TryGetValue(key, out var raw) && raw is T typed)
        {
            return typed;
        }

        return defaultValue;
    }

    /// <summary>True when the dimension carries a metadata entry under <paramref name="key"/>.</summary>
    /// <param name="dimension">Dimension to inspect.</param>
    /// <param name="key">Metadata key.</param>
    /// <returns>Whether the key exists in the dimension's metadata.</returns>
    public static bool HasMetadata(this IDimension dimension, string key)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(key);
        return dimension.Metadata.ContainsKey(key);
    }
}
