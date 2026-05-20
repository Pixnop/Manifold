using System;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace Manifold.Internal.Util;

/// <summary>
/// Validates <see cref="AssetLocation"/> codes used to identify dimensions in Manifold's API.
/// </summary>
internal static partial class DimensionCodeValidator
{
    /// <summary>Domain name reserved for built-in Manifold dimensions.</summary>
    internal const string ReservedDomain = "manifold";

    /// <summary>Validates a consumer-supplied code. The reserved domain <c>manifold</c> is rejected.</summary>
    /// <param name="code">The code to validate.</param>
    /// <exception cref="ArgumentNullException">code is null.</exception>
    /// <exception cref="ArgumentException">code has invalid shape or uses the reserved domain.</exception>
    public static void Validate(AssetLocation code)
    {
        ValidateShape(code);
        if (code.Domain == ReservedDomain)
        {
            throw new ArgumentException(
                $"Domain 'manifold' is reserved for built-in dimensions; got '{code}'.",
                nameof(code));
        }
    }

    /// <summary>Validates a Manifold-internal code; the reserved domain is allowed.</summary>
    /// <param name="code">The code to validate.</param>
    /// <exception cref="ArgumentNullException">code is null.</exception>
    /// <exception cref="ArgumentException">code has invalid shape.</exception>
    public static void ValidateInternal(AssetLocation code) => ValidateShape(code);

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex SegmentRegex();

    private static void ValidateShape(AssetLocation code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (string.IsNullOrWhiteSpace(code.Domain) || string.IsNullOrWhiteSpace(code.Path))
        {
            throw new ArgumentException(
                $"Dimension code must have domain:path; got '{code}'.",
                nameof(code));
        }

        if (!SegmentRegex().IsMatch(code.Domain))
        {
            throw new ArgumentException(
                $"Domain '{code.Domain}' must match [a-z0-9_]+ (got '{code}').",
                nameof(code));
        }

        if (!SegmentRegex().IsMatch(code.Path))
        {
            throw new ArgumentException(
                $"Path '{code.Path}' must match [a-z0-9_]+ (got '{code}').",
                nameof(code));
        }
    }
}
