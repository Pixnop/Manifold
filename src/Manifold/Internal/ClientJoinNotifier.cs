using System;
using Manifold.Api.Events;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Raises one synthetic "the local player is in this dimension" notification when the local player
/// joins a world while already inside a custom (non-overworld) dimension.
/// </summary>
/// <remarks>
/// Client-side, main thread. A join is only knowable once two independent facts have arrived, in
/// either order: the client dimension mirror holds the manifest snapshot, and the local player
/// entity exists with its position. <see cref="Notify"/> is called whenever either may have just
/// become true; it does nothing until both hold, then decides once (raise, or stay silent for an
/// overworld join or an unknown dimension id) and ignores every later call, so a re-sent snapshot
/// can never raise a second notification. The snapshot always carries the overworld (id 0), which
/// is how the mirror's "snapshot received" state is detected without a separate flag.
/// </remarks>
internal sealed class ClientJoinNotifier
{
    private readonly ClientDimensionMirror _mirror;
    private readonly Func<EntityPos?> _localPlayerPosition;
    private readonly ILogger? _logger;
    private bool _decided;

    /// <summary>Initializes a new instance of the <see cref="ClientJoinNotifier"/> class.</summary>
    /// <param name="mirror">Client dimension mirror used to resolve the player's dimension and the overworld.</param>
    /// <param name="localPlayerPosition">
    /// Returns the local player entity's current position (dimension included), or <c>null</c> while
    /// the local player entity does not exist yet.
    /// </param>
    /// <param name="logger">Optional logger for a player standing in a dimension unknown to the mirror.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> or <paramref name="localPlayerPosition"/> is null.</exception>
    public ClientJoinNotifier(ClientDimensionMirror mirror, Func<EntityPos?> localPlayerPosition, ILogger? logger = null)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _localPlayerPosition = localPlayerPosition ?? throw new ArgumentNullException(nameof(localPlayerPosition));
        _logger = logger;
    }

    /// <summary>Raised at most once, on the main thread, for a join inside a custom dimension.</summary>
    public event Action<LocalPlayerDimensionChangedEventArgs>? Joined;

    /// <summary>
    /// Marks the join as decided without raising anything. Called when a real transit has been raised:
    /// the player's dimension is then already reported by that event, so a later <see cref="Notify"/>
    /// must not raise a stale join on top of it.
    /// </summary>
    public void Suppress() => _decided = true;

    /// <summary>
    /// Re-evaluates the join state. Call after the manifest snapshot has been applied to the mirror
    /// and whenever the local player entity may have just become available. Idempotent once decided.
    /// </summary>
    public void Notify()
    {
        if (_decided || _mirror.GetByInternalId(0) is not { } overworld || _localPlayerPosition() is not { } position)
        {
            return;
        }

        _decided = true;
        if (position.Dimension == 0)
        {
            return;
        }

        int dimensionId = position.Dimension;
        var target = _mirror.GetByInternalId(dimensionId);
        if (target is null)
        {
            _logger?.Warning(
                "[Manifold] The local player joined in dimension id {0}, which is not in the client mirror; no join notification raised.",
                dimensionId);
            return;
        }

        var blockPos = new BlockPos((int)position.X, (int)position.Y, (int)position.Z, dimensionId);
        Joined?.Invoke(new LocalPlayerDimensionChangedEventArgs(overworld, target, blockPos, isJoin: true));
    }
}
