using Manifold.Api;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionBuilderImplTests
{
    [Fact]
    public void RegisterStatic_Should_Default_To_Persistent_When_Lifetime_Not_Specified()
    {
        var completion = Substitute.For<IDimension>();
        completion.Code.Returns(Code("mod:a"));
        completion.OwnerModId.Returns("mod");
        completion.Lifetime.Returns(DimensionLifetime.Persistent);

        DimensionBuildRequest? capturedRequest = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            capturedRequest = req;
            return completion;
        });

        var dim = builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.NotNull(capturedRequest);
        Assert.Equal(DimensionLifetime.Persistent, capturedRequest.Value.Lifetime);
        Assert.True(capturedRequest.Value.IsStaticRegistration);
        Assert.NotNull(dim);
    }

    [Fact]
    public void Create_Should_Throw_When_Lifetime_Not_Specified()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<DimensionLifetimeUnspecifiedException>(
            () => builder.WithWorldgen(new FakeWorldgenStrategy()).Create());
    }

    [Fact]
    public void Create_Should_Pass_Through_Persistent_Choice()
    {
        DimensionBuildRequest? capturedRequest = null;
        var completion = Substitute.For<IDimension>();
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            capturedRequest = req;
            return completion;
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).Persistent().Create();

        Assert.NotNull(capturedRequest);
        Assert.Equal(DimensionLifetime.Persistent, capturedRequest.Value.Lifetime);
        Assert.False(capturedRequest.Value.IsStaticRegistration);
    }

    [Fact]
    public void Create_Should_Pass_Through_Ephemeral_Choice()
    {
        DimensionBuildRequest? capturedRequest = null;
        var completion = Substitute.For<IDimension>();
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            capturedRequest = req;
            return completion;
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();

        Assert.NotNull(capturedRequest);
        Assert.Equal(DimensionLifetime.Ephemeral, capturedRequest.Value.Lifetime);
    }

    [Fact]
    public void Persistent_And_Ephemeral_Should_Be_Mutually_Exclusive()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<System.InvalidOperationException>(
            () => builder.Persistent().Ephemeral());

        var builder2 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<System.InvalidOperationException>(
            () => builder2.Ephemeral().Persistent());
    }

    [Fact]
    public void RegisterStatic_Should_Throw_When_Worldgen_Missing()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<WorldgenStrategyContractException>(() => builder.RegisterStatic());
    }

    [Fact]
    public void Create_Should_Throw_When_Worldgen_Missing()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<WorldgenStrategyContractException>(() => builder.Ephemeral().Create());
    }

    [Fact]
    public void Builder_Should_Not_Allow_Reuse_After_RegisterStatic()
    {
        var completion = Substitute.For<IDimension>();
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => completion);
        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        Assert.Throws<System.InvalidOperationException>(() => builder.RegisterStatic());
        Assert.Throws<System.InvalidOperationException>(() => builder.Create());
    }

    [Fact]
    public void RegisterStatic_Should_Throw_When_Ephemeral_Lifetime_Set()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<System.InvalidOperationException>(
            () => builder.WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().RegisterStatic());
    }

    [Fact]
    public void WithGenerationRadius_Should_Pass_Value_Through_To_Request()
    {
        DimensionBuildRequest? capturedRequest = null;
        var completion = Substitute.For<IDimension>();
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            capturedRequest = req;
            return completion;
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).WithGenerationRadius(5).RegisterStatic();

        Assert.NotNull(capturedRequest);
        Assert.Equal(5, capturedRequest.Value.GenerationRadius);
    }

    [Fact]
    public void WithGenerationRadius_Should_Default_To_DefaultGenerationRadius()
    {
        DimensionBuildRequest? capturedRequest = null;
        var completion = Substitute.For<IDimension>();
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            capturedRequest = req;
            return completion;
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.NotNull(capturedRequest);
        Assert.Equal(DimensionBuilderImpl.DefaultGenerationRadius, capturedRequest.Value.GenerationRadius);
    }

    [Fact]
    public void WithGenerationRadius_Should_Throw_When_Out_Of_Range()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.WithGenerationRadius(-1));

        var builder2 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder2.WithGenerationRadius(17));
    }

    [Fact]
    public void WithGenerationRadius_Should_Accept_Boundary_Values()
    {
        DimensionBuildRequest? req0 = null;
        var b0 = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            req0 = req;
            return Substitute.For<IDimension>();
        });
        b0.WithWorldgen(new FakeWorldgenStrategy()).WithGenerationRadius(0).RegisterStatic();
        Assert.Equal(0, req0!.Value.GenerationRadius);

        DimensionBuildRequest? req16 = null;
        var b16 = new DimensionBuilderImpl(Code("mod:b"), "mod", req =>
        {
            req16 = req;
            return Substitute.For<IDimension>();
        });
        b16.WithWorldgen(new FakeWorldgenStrategy()).WithGenerationRadius(16).RegisterStatic();
        Assert.Equal(16, req16!.Value.GenerationRadius);
    }

    [Fact]
    public void WithSpawnBehavior_Should_Pass_Through_To_Request()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithSpawnBehavior(Manifold.Api.Transitions.SpawnBehavior.LastVisited)
            .RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(Manifold.Api.Transitions.SpawnBehavior.LastVisited, captured.Value.SpawnBehavior);
    }

    [Fact]
    public void WithFixedSpawn_Should_Set_DimensionSpawn_And_Point()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        var spawn = new Vintagestory.API.MathTools.BlockPos(10, 64, 20, 0);
        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithFixedSpawn(spawn)
            .RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(Manifold.Api.Transitions.SpawnBehavior.DimensionSpawn, captured.Value.SpawnBehavior);
        Assert.Equal(spawn, captured.Value.SpawnPoint);
    }

    [Fact]
    public void WithForcedGameMode_Should_Pass_Through_To_Request()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithForcedGameMode(EnumGameMode.Creative)
            .RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(EnumGameMode.Creative, captured.Value.ForcedGameMode);
    }

    [Fact]
    public void SpawnBehavior_Should_Default_To_SameCoordinates_And_GameMode_Null()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(Manifold.Api.Transitions.SpawnBehavior.SameCoordinates, captured.Value.SpawnBehavior);
        Assert.Null(captured.Value.SpawnPoint);
        Assert.Null(captured.Value.ForcedGameMode);
    }

    [Fact]
    public void Streaming_Should_Set_StreamingLoadRadius_On_Request()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).Streaming(5).RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(5, captured.Value.StreamingLoadRadius);
    }

    [Fact]
    public void StreamingLoadRadius_Should_Default_To_Null()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.NotNull(captured);
        Assert.Null(captured.Value.StreamingLoadRadius);
    }

    [Fact]
    public void Streaming_Should_Throw_When_Out_Of_Range()
    {
        var b1 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(() => b1.Streaming(0));

        var b2 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(() => b2.Streaming(33));
    }

    [Fact]
    public void WithRelightHeight_Should_Pass_Value_Through_To_Request()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).WithRelightHeight(40).RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(40, captured.Value.RelightHeight);
    }

    [Fact]
    public void RelightHeight_Should_Default_To_DefaultRelightHeight()
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.NotNull(captured);
        Assert.Equal(DimensionBuilderImpl.DefaultRelightHeight, captured.Value.RelightHeight);
    }

    [Fact]
    public void WithRelightHeight_Should_Throw_When_Out_Of_Range()
    {
        var b1 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(() => b1.WithRelightHeight(0));
    }

    private static AssetLocation Code(string s) => new(s);
}
