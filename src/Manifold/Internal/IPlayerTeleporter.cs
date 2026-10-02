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
}
