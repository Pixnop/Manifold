using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Thin abstraction over <see cref="IServerPlayer"/> teleport so tests can verify the
/// <see cref="TransitService"/> calls the teleport with the right args without instantiating
/// a real <c>EntityPlayer</c>.
/// </summary>
internal interface IPlayerTeleporter
{
    /// <summary>Move <paramref name="player"/> to the centre of the block at <paramref name="target"/> (dimension encoded in BlockPos).</summary>
    /// <param name="player">Player to teleport.</param>
    /// <param name="target">Target position with dim encoding.</param>
    /// <param name="yaw">The yaw, in radians, to face on arrival; <c>null</c> keeps the current yaw.</param>
    void Teleport(IServerPlayer player, BlockPos target, float? yaw = null);

    /// <summary>Move <paramref name="player"/> to the exact position, no centring and no rounding.</summary>
    /// <param name="player">Player to teleport.</param>
    /// <param name="dimension">Engine id of the target dimension.</param>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y (dimension-local).</param>
    /// <param name="z">World Z.</param>
    /// <param name="yaw">The yaw, in radians, to face on arrival; <c>null</c> keeps the current yaw.</param>
    void TeleportExact(IServerPlayer player, int dimension, double x, double y, double z, float? yaw);

    /// <summary>
    /// The landing the last teleport of <paramref name="player"/> asked for, when the engine has not
    /// applied it yet. It does so once the destination column is loaded, which can be some ticks
    /// after <see cref="Teleport"/> returns; until then the player's entity already reports the new
    /// dimension but still the coordinates it left.
    /// </summary>
    /// <param name="player">Player to look up.</param>
    /// <returns>The pending landing, or <c>null</c> when none is waiting.</returns>
    PendingLanding? GetPendingLanding(IServerPlayer player);

    /// <summary>
    /// Forgets everything pending for <paramref name="player"/>: they disconnected, so completions
    /// the engine still runs for their old entity must not apply anything.
    /// </summary>
    /// <param name="player">Player to forget.</param>
    void Forget(IServerPlayer player);
}
