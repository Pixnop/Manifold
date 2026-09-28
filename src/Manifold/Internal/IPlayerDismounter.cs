using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Dismounts a player from whatever they are riding before a cross-dimension transit.</summary>
/// <remarks>Server-side, main thread. Abstracted so <see cref="TransitService"/> stays unit-testable.</remarks>
internal interface IPlayerDismounter
{
    /// <summary>
    /// Dismounts <paramref name="player"/> if they are currently mounted (a seat, a saddle, a
    /// boat); a no-op if they are not. The mount itself is left where it is - only the player is
    /// released from it before the transit moves them on.
    /// </summary>
    /// <param name="player">The player to dismount.</param>
    void Dismount(IServerPlayer player);
}
