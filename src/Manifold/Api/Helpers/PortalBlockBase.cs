using System.Collections.Generic;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Internal;
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
    // Targets already warned about (missing/inactive, or a failed transit), so a stuck portal logs
    // once instead of on every physics tick a player keeps touching it. Block instances are shared
    // across every placed block of this type but only ever driven from the server's main thread.
    private readonly HashSet<string> _warnedTargets = new();

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

        TryTeleport(manifold, sapi, player);
    }

    /// <summary>
    /// Core of the collision handler, factored out of <see cref="OnEntityCollide"/> for testability
    /// (no engine <c>Block</c>/<c>Entity</c> plumbing needed to exercise it). A cancelled or
    /// impossible transit must not throw out of the engine callback nor spam the log: the target's
    /// state is checked up front instead of letting <c>TeleportPlayer</c>'s
    /// <see cref="DimensionNotFoundException"/>/<see cref="DimensionStateException"/> escape on every
    /// physics tick a player touches a portal to a gone dimension, and either outcome is logged at
    /// most once per target.
    /// </summary>
    /// <param name="manifold">Manifold's server facade.</param>
    /// <param name="sapi">Server API (used for logging).</param>
    /// <param name="player">The colliding player.</param>
    internal void TryTeleport(IManifoldServer manifold, ICoreServerAPI sapi, IServerPlayer player)
    {
        var target = manifold.Registry.Get(TargetDimensionCode);
        if (target is not { State: DimensionState.Active })
        {
            WarnOnce(sapi, $"[Manifold] Portal to '{TargetDimensionCode}' is missing or inactive; ignoring collision.");
            return;
        }

        try
        {
            if (manifold.Transitions is TransitService core)
            {
                core.TryTeleportPlayer(player, TargetDimensionCode, Options);
            }
            else
            {
                manifold.Transitions.TeleportPlayer(player, TargetDimensionCode, Options);
            }
        }
        catch (ManifoldException ex)
        {
            WarnOnce(sapi, $"[Manifold] Portal to '{TargetDimensionCode}' failed: {ex.Message}");
        }
    }

    private void WarnOnce(ICoreServerAPI sapi, string message)
    {
        if (_warnedTargets.Add(TargetDimensionCode.ToString()))
        {
            sapi.Logger.Warning(message);
        }
    }
}
