using System.Collections.Generic;
using Manifold.Api.Events;
using Manifold.Internal.Networking;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace Manifold.Pure.Tests;

/// <summary>
/// Drives the real <see cref="ManifoldModSystem.StartClientSide"/> wiring: the join notification
/// only ever fires through the manifest subscription and the engine's PlayerEntitySpawn subscription
/// made there, so each test fails if its subscription is missing.
/// </summary>
[Collection("ManifoldAccess")]
public sealed class ManifoldModSystemClientWiringTests
{
    [Fact]
    public void StartClientSide_Should_Raise_The_Join_When_The_Player_Spawns_After_The_Snapshot()
    {
        using var rig = new Rig();
        rig.ReceiveSnapshot();
        Assert.Empty(rig.Received);

        rig.SpawnPlayerEntity();

        Assert.Single(rig.Received);
    }

    [Fact]
    public void StartClientSide_Should_Raise_The_Join_When_The_Snapshot_Arrives_After_The_Player_Spawns()
    {
        using var rig = new Rig();
        rig.SpawnPlayerEntity();
        Assert.Empty(rig.Received);

        rig.ReceiveSnapshot();

        var join = Assert.Single(rig.Received);
        Assert.True(join.IsJoin);
        Assert.Equal("mod:nether", join.Target.Code.ToString());
    }

    private sealed class Rig : System.IDisposable
    {
        private readonly ManifoldModSystem _system = new();
        private readonly ICoreClientAPI _capi = Substitute.For<ICoreClientAPI>();
        private readonly IClientPlayer _player = Substitute.For<IClientPlayer>();
        private NetworkServerMessageHandler<ManifestSnapshotPacket>? _onManifest;

        public Rig()
        {
            var channel = Substitute.For<IClientNetworkChannel>();
            _capi.Network.RegisterChannel(Arg.Any<string>()).Returns(channel);
            channel.RegisterMessageType<DimensionAddedPacket>().Returns(channel);
            channel.RegisterMessageType<DimensionRemovedPacket>().Returns(channel);
            channel.RegisterMessageType<ManifestSnapshotPacket>().Returns(channel);
            channel.RegisterMessageType<PlayerTransitedPacket>().Returns(channel);
            channel.SetMessageHandler(Arg.Any<NetworkServerMessageHandler<DimensionAddedPacket>>()).Returns(channel);
            channel.SetMessageHandler(Arg.Any<NetworkServerMessageHandler<DimensionRemovedPacket>>()).Returns(channel);
            channel.SetMessageHandler(Arg.Any<NetworkServerMessageHandler<PlayerTransitedPacket>>()).Returns(channel);
            channel.SetMessageHandler(Arg.Do<NetworkServerMessageHandler<ManifestSnapshotPacket>>(h => _onManifest = h)).Returns(channel);

            _player.Entity.Returns((EntityPlayer?)null);
            _capi.World.Player.Returns(_player);

            _system.StartClientSide(_capi);
            _system.ClientFacade!.LocalPlayerChangedDimension += (_, e) => Received.Add(e);
        }

        public List<LocalPlayerDimensionChangedEventArgs> Received { get; } = new();

        public void ReceiveSnapshot()
        {
            var packet = new ManifestSnapshotPacket();
            packet.Dimensions.Add(new DimensionDescriptor { Code = "manifold:overworld", InternalId = 0, OwnerModId = "manifold" });
            packet.Dimensions.Add(new DimensionDescriptor { Code = "mod:nether", InternalId = 10, OwnerModId = "mod" });
            _onManifest!(packet);
        }

        public void SpawnPlayerEntity()
        {
            var entity = new EntityPlayer();
            entity.Pos.Dimension = 10;
            _player.Entity.Returns(entity);
            _capi.Event.PlayerEntitySpawn += Raise.Event<PlayerEventDelegate>(_player);
        }

        public void Dispose() => _system.Dispose();
    }
}
