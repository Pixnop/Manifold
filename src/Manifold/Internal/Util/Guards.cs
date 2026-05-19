using System;

namespace Manifold.Internal.Util;

/// <summary>
/// Argument validation helpers used at all public-API boundary methods.
/// All methods return the value to enable fluent use:
/// <c>this.Foo = Guards.NotNullOrWhiteSpace(foo, nameof(foo));</c>.
/// </summary>
internal static class Guards
{
    /// <summary>Validates that a string is non-null and non-whitespace; returns it on success.</summary>
    /// <param name="value">The string to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <returns>The validated string.</returns>
    public static string NotNullOrWhiteSpace(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty or whitespace.", paramName);
        }

        return value;
    }

    /// <summary>Validates that an int is in the inclusive [min, max] range; returns it on success.</summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="min">The minimum allowed value.</param>
    /// <param name="max">The maximum allowed value.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <returns>The validated value.</returns>
    public static int InRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be in [{min}, {max}].");
        }

        return value;
    }
}
