using System;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace Manifold.Internal.Networking;

/// <summary>
/// Registers Manifold's network channel on both sides and exposes typed send/receive APIs.
/// </summary>
/// <remarks>
/// Server-side: register before <c>PlayerJoin</c> handler is wired.
/// Client-side: register in <c>StartClientSide</c>; message handlers raise events the facade subscribes to.
/// </remarks>
internal sealed class ManifoldNetworkChannel
{
    /// <summary>Channel name used on both sides.</summary>
    internal const string ChannelName = "manifold:dims";

    private IServerNetworkChannel? _server;

    /// <summary>Raised on the client when a <see cref="DimensionAddedPacket"/> is received.</summary>
    public event Action<DimensionAddedPacket>? OnClientDimensionAdded;

    /// <summary>Raised on the client when a <see cref="DimensionRemovedPacket"/> is received.</summary>
    public event Action<DimensionRemovedPacket>? OnClientDimensionRemoved;

    /// <summary>Raised on the client when a full <see cref="ManifestSnapshotPacket"/> is received (at join).</summary>
    public event Action<ManifestSnapshotPacket>? OnClientManifest;

    /// <summary>Raised on the client when a <see cref="PlayerTransitedPacket"/> is received.</summary>
    public event Action<PlayerTransitedPacket>? OnClientPlayerTransited;

    /// <summary>Register the channel on the server side.</summary>
    /// <param name="sapi">Server API.</param>
    public void RegisterServer(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        _server = sapi.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<DimensionAddedPacket>()
            .RegisterMessageType<DimensionRemovedPacket>()
            .RegisterMessageType<ManifestSnapshotPacket>()
            .RegisterMessageType<PlayerTransitedPacket>();
    }

    /// <summary>Register the channel on the client side and wire incoming message handlers.</summary>
    /// <param name="capi">Client API.</param>
    public void RegisterClient(ICoreClientAPI capi)
    {
        ArgumentNullException.ThrowIfNull(capi);
        capi.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<DimensionAddedPacket>()
            .RegisterMessageType<DimensionRemovedPacket>()
            .RegisterMessageType<ManifestSnapshotPacket>()
            .RegisterMessageType<PlayerTransitedPacket>()
            .SetMessageHandler<DimensionAddedPacket>(p => OnClientDimensionAdded?.Invoke(p))
            .SetMessageHandler<DimensionRemovedPacket>(p => OnClientDimensionRemoved?.Invoke(p))
            .SetMessageHandler<ManifestSnapshotPacket>(p => OnClientManifest?.Invoke(p))
            .SetMessageHandler<PlayerTransitedPacket>(p => OnClientPlayerTransited?.Invoke(p));
    }

    /// <summary>Broadcast a <see cref="DimensionAddedPacket"/> to all connected clients.</summary>
    /// <param name="packet">Packet to send.</param>
    public void BroadcastDimensionAdded(DimensionAddedPacket packet) =>
        _server?.BroadcastPacket(packet);

    /// <summary>Broadcast a <see cref="DimensionRemovedPacket"/> to all connected clients.</summary>
    /// <param name="packet">Packet to send.</param>
    public void BroadcastDimensionRemoved(DimensionRemovedPacket packet) =>
        _server?.BroadcastPacket(packet);

    /// <summary>Send a <see cref="ManifestSnapshotPacket"/> to a specific player.</summary>
    /// <param name="player">Target player.</param>
    /// <param name="packet">Packet to send.</param>
    public void SendManifestSnapshot(IServerPlayer player, ManifestSnapshotPacket packet) =>
        _server?.SendPacket(packet, player);

    /// <summary>Send a <see cref="PlayerTransitedPacket"/> to a specific player.</summary>
    /// <param name="player">Target player.</param>
    /// <param name="packet">Packet to send.</param>
    public void SendPlayerTransited(IServerPlayer player, PlayerTransitedPacket packet) =>
        _server?.SendPacket(packet, player);
}
