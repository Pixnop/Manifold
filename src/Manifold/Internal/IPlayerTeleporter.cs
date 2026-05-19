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
    /// <summary>Move <paramref name="player"/> to <paramref name="target"/> (dimension encoded in BlockPos).</summary>
    /// <param name="player">Player to teleport.</param>
    /// <param name="target">Target position with dim encoding.</param>
    void Teleport(IServerPlayer player, BlockPos target);
}
