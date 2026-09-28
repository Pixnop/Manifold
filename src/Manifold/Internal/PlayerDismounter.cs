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
    public void Dismount(IServerPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Entity.MountedOn is null)
        {
            return;
        }

        // TryUnmount can refuse (IMountableSeat.CanUnmount returning false - a seat with its own
        // release rule). The transit has already started and must not get stuck waiting on a mount,
        // so a refusal is logged and the transit proceeds anyway: rare, and still better than
        // blocking the whole transit on a mount's say-so.
        if (!player.Entity.TryUnmount())
        {
            _sapi.Logger?.Warning(
                "[Manifold] {0} could not be cleanly dismounted before transit (the mount refused to release them); continuing anyway.",
                player.PlayerName);
        }
    }
}
