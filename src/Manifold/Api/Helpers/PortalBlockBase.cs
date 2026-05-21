using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Helpers;

/// <summary>
/// Abstract base for a block that, when an <see cref="IServerPlayer"/> collides with it,
/// teleports the player to <see cref="TargetDimensionCode"/> via Manifold's transition service.
/// </summary>
/// <remarks>
/// <para>Server-side behaviour only - consumers subclass and override <see cref="TargetDimensionCode"/>
/// (and optionally <see cref="Options"/>).</para>
/// <para>Manifold does not register any portal block itself; this is opt-in.</para>
/// </remarks>
public abstract class PortalBlockBase : Block
{
    /// <summary>Dimension code to transit the player to.</summary>
    protected abstract AssetLocation TargetDimensionCode { get; }

    /// <summary>Optional transition tuning. Default: <see cref="TransitionOptions"/> defaults.</summary>
    protected virtual TransitionOptions Options => default;

    /// <inheritdoc/>
    public override void OnEntityCollide(
        IWorldAccessor world,
        Entity entity,
        BlockPos pos,
        BlockFacing facing,
        Vec3d collideSpeed,
        bool isImpact)
    {
        base.OnEntityCollide(world, entity, pos, facing, collideSpeed, isImpact);

        if (world.Api is not ICoreServerAPI sapi)
        {
            return;
        }

        if (entity is not EntityPlayer ep || ep.Player is not IServerPlayer player)
        {
            return;
        }

        var manifold = ManifoldAccess.GetServer(sapi);
        if (manifold is null || !manifold.IsHealthy)
        {
            return;
        }

        manifold.Transitions.TeleportPlayer(player, TargetDimensionCode, Options);
    }
}
