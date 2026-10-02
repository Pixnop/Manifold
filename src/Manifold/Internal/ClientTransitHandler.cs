using System;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Internal.Networking;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Turns an incoming <see cref="PlayerTransitedPacket"/> into a <see cref="LocalPlayerDimensionChangedEventArgs"/>
/// raised on the client main thread, resolving both dimension codes through the client mirror.
/// </summary>
/// <remarks>
/// Client-side. <see cref="Handle"/> resolves and raises <see cref="Transited"/> synchronously: the
/// engine already delivers every mod channel message on the client main thread (only a handful of
/// low-level packet ids are processed off it), so <see cref="Transited"/> always fires on the main
/// thread as <see cref="Manifold.Api.Client.IManifoldClient.LocalPlayerChangedDimension"/> documents,
/// with no extra dispatch needed.
/// </remarks>
internal sealed class ClientTransitHandler
{
    private readonly ClientDimensionMirror _mirror;
    private readonly ILogger? _logger;
    private readonly Action<float>? _applyYaw;

    /// <summary>Initializes a new instance of the <see cref="ClientTransitHandler"/> class.</summary>
    /// <param name="mirror">Client dimension mirror used to resolve the packet's source/target codes.</param>
    /// <param name="logger">Optional logger for a packet naming a dimension unknown to the mirror.</param>
    /// <param name="applyYaw">Optional action that turns the local player's camera to a yaw (radians); invoked when a packet carries one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> is null.</exception>
    public ClientTransitHandler(ClientDimensionMirror mirror, ILogger? logger = null, Action<float>? applyYaw = null)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _logger = logger;
        _applyYaw = applyYaw;
    }

    /// <summary>Raised on the main thread once a transit packet resolves to two known dimensions.</summary>
    public event Action<LocalPlayerDimensionChangedEventArgs>? Transited;

    /// <summary>
    /// Resolves the packet's source and target codes and raises <see cref="Transited"/>. If either
    /// code is unknown to the mirror - for example the manifest snapshot has not arrived yet, or a
    /// resync race - the packet is dropped: no event is raised, and a warning is logged if a logger
    /// was supplied. This deliberately mirrors the null-check contract of
    /// <see cref="LocalPlayerDimensionChangedEventArgs"/>'s constructor, which requires both dimensions.
    /// </summary>
    /// <param name="packet">The packet received on the network channel.</param>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is null.</exception>
    /// <remarks>
    /// A yaw the packet carries is applied first and regardless of whether its dimensions resolve:
    /// facing the requested way does not depend on the client mirror knowing either dimension.
    /// </remarks>
    public void Handle(PlayerTransitedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Yaw is { } yaw)
        {
            _applyYaw?.Invoke(yaw);
        }

        var source = _mirror.Get(new AssetLocation(packet.SourceCode));
        var target = _mirror.Get(new AssetLocation(packet.TargetCode));
        if (source is null || target is null)
        {
            _logger?.Warning(
                "[Manifold] Dropped a local player transit notification: '{0}' -> '{1}' names a dimension not (yet) known to the client mirror.",
                packet.SourceCode,
                packet.TargetCode);
            return;
        }

        var targetPosition = new BlockPos(packet.TargetX, packet.TargetY, packet.TargetZ, target.InternalId);
        Transited?.Invoke(new LocalPlayerDimensionChangedEventArgs(source, target, targetPosition, isJoin: false, packet.IsRespawn));
    }
}
