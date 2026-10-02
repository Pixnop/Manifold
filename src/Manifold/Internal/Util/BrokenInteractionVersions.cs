using System;
using Vintagestory.API.Config;

namespace Manifold.Internal.Util;

/// <summary>
/// Game versions whose interaction range check is broken in every dimension but the overworld
/// (issue #79): it measured the player's eye with the internal Y (local Y plus 32768 per dimension)
/// against the block's local Y, so containers closed as soon as they opened and block interactions
/// were refused as out of range. Fixed by the game in 1.22.6.
/// </summary>
internal static class BrokenInteractionVersions
{
    /// <summary>
    /// The version of the game actually running. <see cref="GameVersion.ShortGameVersion"/> is a
    /// constant, so referencing it directly would bake in the version Manifold was compiled against.
    /// </summary>
    /// <returns>The running game's short version, or null if it cannot be read.</returns>
    public static string? RunningGameVersion() =>
        typeof(GameVersion).GetField(nameof(GameVersion.ShortGameVersion))?.GetRawConstantValue() as string;

    /// <summary>Whether <paramref name="gameVersion"/> has the broken check.</summary>
    /// <param name="gameVersion">A short game version such as <c>1.22.5</c>; null is not affected.</param>
    /// <returns>True for 1.22.4 and 1.22.5.</returns>
    public static bool IsAffected(string? gameVersion) =>
        string.Equals(gameVersion, "1.22.4", StringComparison.Ordinal)
        || string.Equals(gameVersion, "1.22.5", StringComparison.Ordinal);
}
