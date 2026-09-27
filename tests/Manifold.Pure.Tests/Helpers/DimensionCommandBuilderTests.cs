using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Helpers;

/// <summary>
/// Tests for <see cref="DimensionCommandBuilder"/>.
/// </summary>
public sealed class DimensionCommandBuilderTests
{
    /// <summary>
    /// Verifies that the builder defaults the privilege to "chat".
    /// </summary>
    [Fact]
    public void Builder_Should_Default_Privilege_To_Chat()
    {
        var b = new DimensionCommandBuilder();
        Assert.Equal("chat", b.Privilege);
    }

    /// <summary>
    /// Verifies that Validate throws when TargetDimension is not set.
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_TargetDimension_Missing()
    {
        var b = new DimensionCommandBuilder().Command("voiddim");
        Assert.Throws<System.InvalidOperationException>(() => b.Validate());
    }

    /// <summary>
    /// Verifies that Validate throws when Command name is not set.
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_When_Name_Missing()
    {
        var b = new DimensionCommandBuilder().TargetDimension(new AssetLocation("mod:dim"));
        Assert.Throws<System.InvalidOperationException>(() => b.Validate());
    }

    /// <summary>
    /// Verifies that Validate passes when both required fields are set.
    /// </summary>
    [Fact]
    public void Validate_Should_Pass_When_Name_And_Target_Set()
    {
        new DimensionCommandBuilder()
            .Command("voiddim")
            .TargetDimension(new AssetLocation("mod:dim"))
            .Validate();
    }

    /// <summary>
    /// Verifies that builder methods are chainable.
    /// </summary>
    [Fact]
    public void Builder_Methods_Should_Be_Chainable()
    {
        var b = new DimensionCommandBuilder()
            .Command("test")
            .TargetDimension(new AssetLocation("mod:t"))
            .RequiresPrivilege("controlserver")
            .DescribedAs("Custom desc");
        Assert.Equal("test", b.Name);
        Assert.Equal("controlserver", b.Privilege);
        Assert.Equal("Custom desc", b.Description);
    }

    /// <summary>
    /// A transit blocked by a missing/inactive target must reply with an error naming the failure,
    /// not let the exception escape the command handler.
    /// </summary>
    [Fact]
    public void TryTeleport_Should_Reply_Error_When_Target_Missing()
    {
        var manifold = Substitute.For<IManifoldServer>();
        var transitions = Substitute.For<ITransitionService>();
        manifold.Transitions.Returns(transitions);
        transitions
            .When(t => t.TryTeleportPlayer(Arg.Any<IServerPlayer>(), Arg.Any<AssetLocation>(), Arg.Any<TransitionOptions>()))
            .Do(_ => throw new DimensionNotFoundException("No dimension registered with code 'mod:gone'."));

        var result = DimensionCommandBuilder.TryTeleport(
            manifold, Substitute.For<IServerPlayer>(), new AssetLocation("mod:gone"), default);

        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("No dimension registered with code 'mod:gone'.", result.StatusMessage);
    }

    /// <summary>
    /// A transit a subscriber cancels must reply with an error, not the success message a caller
    /// would otherwise send believing the player actually moved.
    /// </summary>
    [Fact]
    public void TryTeleport_Should_Reply_Error_When_Transit_Cancelled()
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        registry.DefineForOwner(new AssetLocation("owner:target"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var sapi = Substitute.For<ICoreServerAPI>();
        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(1, 1, 1, 10));
        var transit = new TransitService(
            registry,
            sapi,
            new TransitMovers(
                Substitute.For<IPlayerTeleporter>(),
                Substitute.For<IEntityMover>(),
                Substitute.For<IBlockMover>()),
            positionResolver,
            new DimensionGenerator(registry, new GeneratedColumnStore()),
            new PlayerPositionStore(),
            Substitute.For<IInventorySwapper>());
        transit.PlayerEntering += (_, e) => e.Cancel = true;

        var manifold = Substitute.For<IManifoldServer>();
        manifold.Transitions.Returns(transit);
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());

        var result = DimensionCommandBuilder.TryTeleport(manifold, player, new AssetLocation("owner:target"), default);

        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("Transit was cancelled.", result.StatusMessage);
    }
}
