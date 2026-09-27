using System;
using Manifold.Api.Events;
using Manifold.Internal;
using Manifold.Internal.Networking;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class ClientTransitHandlerTests
{
    [Fact]
    public void Constructor_Should_Throw_When_Mirror_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => new ClientTransitHandler(null!));
    }

    [Fact]
    public void Handle_Should_Throw_When_Packet_Is_Null()
    {
        var handler = new ClientTransitHandler(new ClientDimensionMirror());

        Assert.Throws<ArgumentNullException>(() => handler.Handle(null!));
    }

    [Fact]
    public void Handle_Should_Resolve_And_Raise_Transited_Synchronously()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        Seed(mirror, "mod:nether", 10);
        var handler = new ClientTransitHandler(mirror);
        LocalPlayerDimensionChangedEventArgs? captured = null;
        handler.Transited += e => captured = e;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "a:overworld", TargetCode = "mod:nether" });

        // No dispatcher indirection: the event is already raised by the time Handle returns.
        Assert.NotNull(captured);
    }

    [Fact]
    public void Handle_Should_Raise_Transited_With_Resolved_Dimensions_And_TargetPosition()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        Seed(mirror, "mod:nether", 10);
        var handler = new ClientTransitHandler(mirror);
        LocalPlayerDimensionChangedEventArgs? captured = null;
        handler.Transited += e => captured = e;

        handler.Handle(new PlayerTransitedPacket
        {
            SourceCode = "a:overworld",
            TargetCode = "mod:nether",
            TargetX = 100,
            TargetY = 6,
            TargetZ = 200,
        });

        Assert.NotNull(captured);
        Assert.Equal("a:overworld", captured!.Source.Code.ToString());
        Assert.Equal("mod:nether", captured.Target.Code.ToString());
        Assert.Equal(100, captured.TargetPosition.X);
        Assert.Equal(6, captured.TargetPosition.Y);
        Assert.Equal(200, captured.TargetPosition.Z);
        Assert.Equal(10, captured.TargetPosition.dimension);
    }

    [Fact]
    public void Handle_Should_Not_Raise_When_The_Target_Code_Is_Unknown_To_The_Mirror()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        var handler = new ClientTransitHandler(mirror);
        bool raised = false;
        handler.Transited += _ => raised = true;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "a:overworld", TargetCode = "mod:unknown" });

        Assert.False(raised);
    }

    [Fact]
    public void Handle_Should_Not_Raise_When_The_Source_Was_Removed_Before_The_Packet_Is_Handled()
    {
        // Documents the ordering hazard behind the transit-out-of-ephemeral bug: reaping the
        // source dimension broadcasts a DimensionRemovedPacket for it, and if a client applies
        // that before the matching PlayerTransitedPacket (the server sent them in the wrong
        // order, or they simply arrive out of order), the source is already gone from the mirror
        // by the time Handle resolves it. This is why ManifoldModSystem now sends the transit
        // packet before reaping: on the correct order, ApplyRemoved runs after Handle, not before.
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        Seed(mirror, "mod:ephemeral", 10);
        mirror.ApplyRemoved(new DimensionRemovedPacket { Code = "mod:ephemeral", InternalId = 10 });
        var handler = new ClientTransitHandler(mirror);
        bool raised = false;
        handler.Transited += _ => raised = true;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "mod:ephemeral", TargetCode = "a:overworld" });

        Assert.False(raised);
    }

    [Fact]
    public void Handle_Should_Not_Raise_When_The_Source_Code_Is_Unknown_To_The_Mirror()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "mod:nether", 10);
        var handler = new ClientTransitHandler(mirror);
        bool raised = false;
        handler.Transited += _ => raised = true;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "mod:unknown", TargetCode = "mod:nether" });

        Assert.False(raised);
    }

    [Fact]
    public void Handle_Should_Log_A_Warning_When_A_Dimension_Is_Unknown_To_The_Mirror()
    {
        var mirror = new ClientDimensionMirror();
        var logger = Substitute.For<ILogger>();
        var handler = new ClientTransitHandler(mirror, logger);

        handler.Handle(new PlayerTransitedPacket { SourceCode = "mod:unknown", TargetCode = "mod:also-unknown" });

        logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    private static void Seed(ClientDimensionMirror mirror, string code, int internalId) =>
        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = code, InternalId = internalId, OwnerModId = "owner" },
        });
}
