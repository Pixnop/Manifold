using System;
using System.Collections.Generic;
using Manifold.Api.Events;
using Manifold.Internal;
using Manifold.Internal.Networking;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>Tests for <see cref="ClientJoinNotifier"/>.</summary>
public sealed class ClientJoinNotifierTests
{
    [Fact]
    public void Constructor_Should_Throw_When_Mirror_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => new ClientJoinNotifier(null!, () => null));
    }

    [Fact]
    public void Constructor_Should_Throw_When_Position_Provider_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => new ClientJoinNotifier(new ClientDimensionMirror(), null!));
    }

    [Fact]
    public void Notify_Should_Raise_Joined_When_The_Player_Joins_Inside_A_Custom_Dimension()
    {
        var mirror = new ClientDimensionMirror();
        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        var notifier = new ClientJoinNotifier(mirror, () => At(10, 5.7, 64.2, -3.5));
        var raised = Capture(notifier);

        notifier.Notify();

        var args = Assert.Single(raised);
        Assert.True(args.IsJoin);
        Assert.Equal("a:overworld", args.Source.Code.ToString());
        Assert.Equal("mod:nether", args.Target.Code.ToString());
        Assert.Equal(5, args.TargetPosition.X);
        Assert.Equal(64, args.TargetPosition.Y);
        Assert.Equal(-3, args.TargetPosition.Z);
        Assert.Equal(10, args.TargetPosition.dimension);
    }

    [Fact]
    public void Notify_Should_Not_Raise_When_The_Player_Joins_In_The_Overworld()
    {
        var mirror = new ClientDimensionMirror();
        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        var notifier = new ClientJoinNotifier(mirror, () => At(0, 1, 70, 1));
        var raised = Capture(notifier);

        notifier.Notify();

        Assert.Empty(raised);
    }

    [Fact]
    public void Notify_Should_Wait_For_The_Player_When_The_Snapshot_Arrives_First()
    {
        var mirror = new ClientDimensionMirror();
        EntityPos? pos = null;
        var notifier = new ClientJoinNotifier(mirror, () => pos);
        var raised = Capture(notifier);

        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        notifier.Notify();
        Assert.Empty(raised);

        pos = At(10, 1, 2, 3);
        notifier.Notify();

        Assert.Single(raised);
    }

    [Fact]
    public void Notify_Should_Wait_For_The_Snapshot_When_The_Player_Is_Ready_First()
    {
        var mirror = new ClientDimensionMirror();
        var notifier = new ClientJoinNotifier(mirror, () => At(10, 1, 2, 3));
        var raised = Capture(notifier);

        notifier.Notify();
        Assert.Empty(raised);

        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        notifier.Notify();

        Assert.Single(raised);
    }

    [Fact]
    public void Notify_Should_Raise_Only_Once_When_The_Snapshot_Is_Received_Twice()
    {
        var mirror = new ClientDimensionMirror();
        var notifier = new ClientJoinNotifier(mirror, () => At(10, 1, 2, 3));
        var raised = Capture(notifier);

        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        notifier.Notify();
        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        notifier.Notify();
        notifier.Notify();

        Assert.Single(raised);
    }

    [Fact]
    public void Notify_Should_Stay_Silent_For_Good_When_The_First_Decision_Was_An_Overworld_Join()
    {
        // A later position change (a real transit, handled elsewhere) must never turn into a join.
        var mirror = new ClientDimensionMirror();
        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        int dimension = 0;
        var notifier = new ClientJoinNotifier(mirror, () => At(dimension, 1, 2, 3));
        var raised = Capture(notifier);

        notifier.Notify();
        dimension = 10;
        notifier.Notify();

        Assert.Empty(raised);
    }

    [Fact]
    public void Notify_Should_Log_A_Warning_Once_And_Not_Raise_When_The_Dimension_Id_Is_Unknown()
    {
        var mirror = new ClientDimensionMirror();
        Snapshot(mirror, ("a:overworld", 0));
        var logger = Substitute.For<ILogger>();
        var notifier = new ClientJoinNotifier(mirror, () => At(99, 1, 2, 3), logger);
        var raised = Capture(notifier);

        notifier.Notify();
        notifier.Notify();

        Assert.Empty(raised);
        logger.Received(1).Warning(
            Arg.Is<string>(s => s.Contains("not in the client mirror", StringComparison.Ordinal)),
            Arg.Any<object[]>());
    }

    [Fact]
    public void Notify_Should_Not_Throw_When_The_Dimension_Id_Is_Unknown_And_There_Is_No_Logger()
    {
        var mirror = new ClientDimensionMirror();
        Snapshot(mirror, ("a:overworld", 0));
        var notifier = new ClientJoinNotifier(mirror, () => At(99, 1, 2, 3));

        notifier.Notify();
    }

    [Fact]
    public void Facade_Should_Republish_The_Join_Through_LocalPlayerChangedDimension()
    {
        var mirror = new ClientDimensionMirror();
        var notifier = new ClientJoinNotifier(mirror, () => At(10, 1, 2, 3));
        var facade = new ManifoldClientFacade(mirror, joinNotifier: notifier);
        object? sender = null;
        LocalPlayerDimensionChangedEventArgs? captured = null;
        facade.LocalPlayerChangedDimension += (s, e) =>
        {
            sender = s;
            captured = e;
        };

        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        notifier.Notify();

        Assert.Same(facade, sender);
        Assert.NotNull(captured);
        Assert.True(captured!.IsJoin);
    }

    [Fact]
    public void Facade_Should_Isolate_A_Throwing_Subscriber_Of_The_Join()
    {
        var mirror = new ClientDimensionMirror();
        var notifier = new ClientJoinNotifier(mirror, () => At(10, 1, 2, 3));
        var facade = new ManifoldClientFacade(mirror, joinNotifier: notifier);
        bool goodRan = false;
        facade.LocalPlayerChangedDimension += (_, _) => throw new InvalidOperationException("rogue mod");
        facade.LocalPlayerChangedDimension += (_, _) => goodRan = true;

        Snapshot(mirror, ("a:overworld", 0), ("mod:nether", 10));
        notifier.Notify();

        Assert.True(goodRan);
    }

    private static EntityPos At(int dimension, double x, double y, double z) =>
        new(x, y, z) { Dimension = dimension };

    private static List<LocalPlayerDimensionChangedEventArgs> Capture(ClientJoinNotifier notifier)
    {
        var raised = new List<LocalPlayerDimensionChangedEventArgs>();
        notifier.Joined += raised.Add;
        return raised;
    }

    private static void Snapshot(ClientDimensionMirror mirror, params (string Code, int Id)[] dimensions)
    {
        var packet = new ManifestSnapshotPacket();
        foreach (var (code, id) in dimensions)
        {
            packet.Dimensions.Add(new DimensionDescriptor { Code = code, InternalId = id, OwnerModId = "mod" });
        }

        mirror.ApplyManifest(packet);
    }
}
