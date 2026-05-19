namespace Manifold.Internal;

/// <summary>
/// Abstraction over the mod loader's "is this mod currently loaded?" query.
/// Production impl wraps <c>sapi.ModLoader.IsModEnabled</c>.
/// </summary>
internal interface IModLoaderQuery
{
    /// <summary>Check whether a mod with the given id is currently loaded.</summary>
    /// <param name="modId">Mod id to query.</param>
    /// <returns><c>true</c> if loaded.</returns>
    bool IsModLoaded(string modId);
}
