using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Production <see cref="IPlayerTeleporter"/> backed by VS engine teleport.</summary>
internal sealed class PlayerTeleporter : IPlayerTeleporter
{
    /// <inheritdoc/>
    public void Teleport(IServerPlayer player, BlockPos target)
    {
        // TeleportTo takes int x, y, z and dimension is already encoded in BlockPos.
        player.Entity.TeleportTo(target.X, target.Y, target.Z);
    }
}
