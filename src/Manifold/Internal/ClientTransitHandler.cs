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
/// Client-side. <see cref="Handle"/> may be called from any thread (the engine invokes network
/// channel message handlers off the main thread, on its own network-processing thread); it hands
/// the actual resolution and event raise to the main-thread dispatcher supplied at construction,
/// so <see cref="Transited"/> always fires on the main thread as
/// <see cref="Manifold.Api.Client.IManifoldClient.LocalPlayerChangedDimension"/> documents.
/// </remarks>
internal sealed class ClientTransitHandler
{
    private readonly ClientDimensionMirror _mirror;
    private readonly Action<Action> _dispatchToMainThread;
    private readonly ILogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="ClientTransitHandler"/> class.</summary>
    /// <param name="mirror">Client dimension mirror used to resolve the packet's source/target codes.</param>
    /// <param name="dispatchToMainThread">
    /// Runs the given action on the client main thread (typically <c>capi.Event.EnqueueMainThreadTask</c>).
    /// </param>
    /// <param name="logger">Optional logger for a packet naming a dimension unknown to the mirror.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> or <paramref name="dispatchToMainThread"/> is null.</exception>
    public ClientTransitHandler(ClientDimensionMirror mirror, Action<Action> dispatchToMainThread, ILogger? logger = null)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _dispatchToMainThread = dispatchToMainThread ?? throw new ArgumentNullException(nameof(dispatchToMainThread));
        _logger = logger;
    }

    /// <summary>Raised on the main thread once a transit packet resolves to two known dimensions.</summary>
    public event Action<LocalPlayerDimensionChangedEventArgs>? Transited;

    /// <summary>
    /// Handles an incoming transit packet: dispatches resolution and the <see cref="Transited"/>
    /// raise to the main thread via the constructor's dispatcher.
    /// </summary>
    /// <param name="packet">The packet received on the network channel.</param>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is null.</exception>
    public void Handle(PlayerTransitedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        _dispatchToMainThread(() => Resolve(packet));
    }

    /// <summary>
    /// Resolves the packet's source and target codes and raises <see cref="Transited"/>. If either
    /// code is unknown to the mirror - for example the manifest snapshot has not arrived yet, or a
    /// resync race - the packet is dropped: no event is raised, and a warning is logged if a logger
    /// was supplied. This deliberately mirrors the null-check contract of
    /// <see cref="LocalPlayerDimensionChangedEventArgs"/>'s constructor, which requires both dimensions.
    /// </summary>
    /// <param name="packet">The packet to resolve.</param>
    private void Resolve(PlayerTransitedPacket packet)
    {
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
        Transited?.Invoke(new LocalPlayerDimensionChangedEventArgs(source, target, targetPosition));
    }
}
