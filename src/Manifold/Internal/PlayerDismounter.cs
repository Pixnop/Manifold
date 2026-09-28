using System;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Production <see cref="IPlayerDismounter"/> backed by <c>EntityAgent.TryUnmount</c>.</summary>
internal sealed class PlayerDismounter : IPlayerDismounter
{
    private readonly ICoreServerAPI _sapi;

    /// <summary>Initializes a new instance of the <see cref="PlayerDismounter"/> class.</summary>
    /// <param name="sapi">Server API, used only to log a refused dismount.</param>
    public PlayerDismounter(ICoreServerAPI sapi) =>
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));

    /// <inheritdoc/>
    public bool Dismount(IServerPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Entity.MountedOn is null)
        {
            return true;
        }

        // TryUnmount can refuse (IMountableSeat.CanUnmount returning false, a seat with its own
        // release rule, e.g. a moving elevator). Nothing has moved yet at this point in a transit,
        // so the caller aborts instead of proceeding: the engine's own TeleportToDouble moves a
        // still-mounted player's mount to the target coordinates in the SOURCE dimension while only
        // the player's own dimension flips, desyncing the mount from its rider.
        if (player.Entity.TryUnmount())
        {
            return true;
        }

        _sapi.Logger?.Warning(
            "[Manifold] {0} could not be dismounted before transit (the mount refused to release them); transit aborted.",
            player.PlayerName);
        return false;
    }
}
