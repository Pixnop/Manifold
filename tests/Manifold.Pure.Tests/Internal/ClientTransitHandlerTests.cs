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
        Assert.Throws<ArgumentNullException>(() => new ClientTransitHandler(null!, _ => { }));
    }

    [Fact]
    public void Constructor_Should_Throw_When_Dispatcher_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => new ClientTransitHandler(new ClientDimensionMirror(), null!));
    }

    [Fact]
    public void Handle_Should_Throw_When_Packet_Is_Null()
    {
        var handler = new ClientTransitHandler(new ClientDimensionMirror(), _ => { });

        Assert.Throws<ArgumentNullException>(() => handler.Handle(null!));
    }

    [Fact]
    public void Handle_Should_Dispatch_Resolution_Through_The_Supplied_Dispatcher_Not_Inline()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        Seed(mirror, "mod:nether", 10);
        bool dispatched = false;
        var handler = new ClientTransitHandler(mirror, action =>
        {
            dispatched = true;
            action();
        });
        LocalPlayerDimensionChangedEventArgs? captured = null;
        handler.Transited += e => captured = e;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "a:overworld", TargetCode = "mod:nether" });

        Assert.True(dispatched);
        Assert.NotNull(captured);
    }

    [Fact]
    public void Handle_Should_Not_Resolve_Or_Raise_Before_The_Dispatcher_Runs_The_Action()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        Seed(mirror, "mod:nether", 10);
        var handler = new ClientTransitHandler(mirror, _ => { /* never runs the action */ });
        bool raised = false;
        handler.Transited += _ => raised = true;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "a:overworld", TargetCode = "mod:nether" });

        Assert.False(raised);
    }

    [Fact]
    public void Handle_Should_Raise_Transited_With_Resolved_Dimensions_And_TargetPosition()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "a:overworld", 0);
        Seed(mirror, "mod:nether", 10);
        var handler = new ClientTransitHandler(mirror, action => action());
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
        var handler = new ClientTransitHandler(mirror, action => action());
        bool raised = false;
        handler.Transited += _ => raised = true;

        handler.Handle(new PlayerTransitedPacket { SourceCode = "a:overworld", TargetCode = "mod:unknown" });

        Assert.False(raised);
    }

    [Fact]
    public void Handle_Should_Not_Raise_When_The_Source_Code_Is_Unknown_To_The_Mirror()
    {
        var mirror = new ClientDimensionMirror();
        Seed(mirror, "mod:nether", 10);
        var handler = new ClientTransitHandler(mirror, action => action());
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
        var handler = new ClientTransitHandler(mirror, action => action(), logger);

        handler.Handle(new PlayerTransitedPacket { SourceCode = "mod:unknown", TargetCode = "mod:also-unknown" });

        logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    private static void Seed(ClientDimensionMirror mirror, string code, int internalId) =>
        mirror.ApplyAdded(new DimensionAddedPacket
        {
            Dimension = new DimensionDescriptor { Code = code, InternalId = internalId, OwnerModId = "owner" },
        });
}
