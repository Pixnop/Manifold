using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="PortalBlockBase"/>. Exercised through the internal <c>TryTeleport</c> core
/// rather than <c>OnEntityCollide</c>, since that core needs no engine <c>Block</c>/<c>Entity</c>
/// plumbing to test.
/// </summary>
public sealed class PortalBlockBaseTests
{
    [Fact]
    public void TryTeleport_Should_Not_Throw_When_Target_Is_Missing()
    {
        var portal = new TestPortal(new AssetLocation("mod:gone"));
        var manifold = Substitute.For<IManifoldServer>();
        manifold.Registry.Get(Arg.Any<AssetLocation>()).Returns((IDimension?)null);
        var sapi = Substitute.For<ICoreServerAPI>();

        portal.TryTeleport(manifold, sapi, Substitute.For<IServerPlayer>());

        manifold.Transitions.DidNotReceive().TeleportPlayer(
            Arg.Any<IServerPlayer>(), Arg.Any<AssetLocation>(), Arg.Any<TransitionOptions>());
    }

    [Fact]
    public void TryTeleport_Should_Warn_Only_Once_Per_Target_When_Missing()
    {
        var portal = new TestPortal(new AssetLocation("mod:gone"));
        var manifold = Substitute.For<IManifoldServer>();
        manifold.Registry.Get(Arg.Any<AssetLocation>()).Returns((IDimension?)null);
        var sapi = Substitute.For<ICoreServerAPI>();
        var player = Substitute.For<IServerPlayer>();

        // Simulates a player standing in the portal across several physics ticks.
        portal.TryTeleport(manifold, sapi, player);
        portal.TryTeleport(manifold, sapi, player);
        portal.TryTeleport(manifold, sapi, player);

        sapi.Logger.Received(1).Warning(Arg.Any<string>());
    }

    [Fact]
    public void TryTeleport_Should_Not_Throw_When_Transit_Throws_A_ManifoldException()
    {
        var portal = new TestPortal(new AssetLocation("owner:target"));
        var manifold = Substitute.For<IManifoldServer>();
        var target = Substitute.For<IDimension>();
        target.State.Returns(DimensionState.Active);
        manifold.Registry.Get(Arg.Any<AssetLocation>()).Returns(target);
        manifold.Transitions
            .When(t => t.TeleportPlayer(Arg.Any<IServerPlayer>(), Arg.Any<AssetLocation>(), Arg.Any<TransitionOptions>()))
            .Do(_ => throw new DimensionStateException("boom"));
        var sapi = Substitute.For<ICoreServerAPI>();

        // Must not throw out of the engine collision callback.
        portal.TryTeleport(manifold, sapi, Substitute.For<IServerPlayer>());

        sapi.Logger.Received(1).Warning(Arg.Any<string>());
    }

    [Fact]
    public void TryTeleport_Should_Teleport_When_Target_Is_Active()
    {
        var portal = new TestPortal(new AssetLocation("owner:target"));
        var manifold = Substitute.For<IManifoldServer>();
        var target = Substitute.For<IDimension>();
        target.State.Returns(DimensionState.Active);
        manifold.Registry.Get(Arg.Any<AssetLocation>()).Returns(target);
        var sapi = Substitute.For<ICoreServerAPI>();
        var player = Substitute.For<IServerPlayer>();

        portal.TryTeleport(manifold, sapi, player);

        manifold.Transitions.Received(1).TeleportPlayer(player, portal.PublicTargetDimensionCode, Arg.Any<TransitionOptions>());
    }

    private sealed class TestPortal : PortalBlockBase
    {
        private readonly AssetLocation _target;

        public TestPortal(AssetLocation target) => _target = target;

        public AssetLocation PublicTargetDimensionCode => _target;

        protected override AssetLocation TargetDimensionCode => _target;
    }
}
