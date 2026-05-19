using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Production <see cref="IPlayerTeleporter"/> backed by VS engine cross-dimension teleport.</summary>
/// <remarks>
/// Cross-dimension transit requires <c>EntityPlayer.ChangeDimension</c> BEFORE positional teleport.
/// Using <c>TeleportTo(EntityPos)</c> or <c>TeleportTo(int, int, int)</c> alone does NOT change dimension —
/// they call <c>LoadChunkColumnPriority</c> without a dimension parameter and the player remains in dim 0.
/// <c>ChangeDimension</c> additionally fires the public <c>IEventAPI.PlayerDimensionChanged</c> event.
/// </remarks>
internal sealed class PlayerTeleporter : IPlayerTeleporter
{
    /// <inheritdoc/>
    public void Teleport(IServerPlayer player, BlockPos target)
    {
        // Step 1: rebind entity to the destination dimension (chunk membership + PlayerDimensionChanged event).
        player.Entity.ChangeDimension(target.dimension);

        // Step 2: positional teleport. +0.5 centers the player on the target block in X/Z.
        player.Entity.TeleportToDouble(target.X + 0.5, target.Y, target.Z + 0.5);
    }
}
