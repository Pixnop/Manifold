using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Dismounts a player from whatever they are riding before a cross-dimension transit.</summary>
/// <remarks>Server-side, main thread. Abstracted so <see cref="TransitService"/> stays unit-testable.</remarks>
internal interface IPlayerDismounter
{
    /// <summary>
    /// Dismounts <paramref name="player"/> if they are currently mounted (a seat, a saddle, a
    /// boat); a no-op (returns <c>true</c>) if they are not. The mount itself is left where it is -
    /// only the player is released from it before the transit moves them on.
    /// </summary>
    /// <param name="player">The player to dismount.</param>
    /// <returns>
    /// <c>true</c> if the player is no longer mounted (they were not mounted, or the mount
    /// released them); <c>false</c> if a mounted player's seat refused to release them (an
    /// <c>IMountableSeat.CanUnmount</c> that returns <c>false</c>).
    /// </returns>
    bool Dismount(IServerPlayer player);
}
