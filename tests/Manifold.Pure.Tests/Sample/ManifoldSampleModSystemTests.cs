using Manifold.Api;
using Manifold.Api.Server;
using ManifoldSample;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Sample;

/// <summary>
/// Tests for <see cref="ManifoldSampleModSystem.HandleMiningDim"/>'s ore-layout salt bump.
/// </summary>
public sealed class ManifoldSampleModSystemTests
{
    [Fact]
    public void HandleMiningDim_Should_Bump_Salt_On_Every_Recreation_Not_Only_On_Reset()
    {
        var modSystem = new ManifoldSampleModSystem();
        var miningCode = new AssetLocation("manifoldsample", "mining");
        var player = Substitute.For<IServerPlayer>();

        var builder = Substitute.For<IDimensionBuilder>();
        builder.Ephemeral().Returns(builder);
        builder.WithWorldgen(Arg.Any<Manifold.Api.Worldgen.IWorldgenStrategy>()).Returns(builder);
        builder.WithFixedSpawn(Arg.Any<Vintagestory.API.MathTools.BlockPos>()).Returns(builder);
        builder.WithGenerationRadius(Arg.Any<int>()).Returns(builder);

        var registry = Substitute.For<IDimensionRegistry>();
        registry.Define(Arg.Any<AssetLocation>()).Returns(builder);

        // Nothing registered under miningCode at any point: exactly what the registry reports
        // both right after an explicit /miningreset and after an unattended auto-reap (last
        // occupant left, or server shutdown) - HandleMiningDim cannot tell those apart, and must
        // not need to.
        registry.Get(miningCode).Returns((IDimension?)null);

        var manifold = Substitute.For<IManifoldServer>();
        manifold.Registry.Returns(registry);

        modSystem.HandleMiningDim(manifold, miningCode, player);
        int saltAfterFirstCreate = modSystem.MiningDimSaltForTests;

        modSystem.HandleMiningDim(manifold, miningCode, player);
        int saltAfterSecondCreate = modSystem.MiningDimSaltForTests;

        Assert.NotEqual(saltAfterFirstCreate, saltAfterSecondCreate);
    }

    [Fact]
    public void HandleMiningDim_Should_Not_Recreate_Or_Bump_Salt_When_Already_Registered()
    {
        var modSystem = new ManifoldSampleModSystem();
        var miningCode = new AssetLocation("manifoldsample", "mining");
        var player = Substitute.For<IServerPlayer>();

        var registry = Substitute.For<IDimensionRegistry>();
        registry.Get(miningCode).Returns(Substitute.For<IDimension>());

        var manifold = Substitute.For<IManifoldServer>();
        manifold.Registry.Returns(registry);

        modSystem.HandleMiningDim(manifold, miningCode, player);

        Assert.Equal(0, modSystem.MiningDimSaltForTests);
        registry.DidNotReceive().Define(Arg.Any<AssetLocation>());
    }
}
