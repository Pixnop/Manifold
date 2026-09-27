using System;
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
        var (builder, request) = Capturing();

        var dim = builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.Equal(DimensionLifetime.Persistent, request().Lifetime);
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
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).Persistent().Create();

        Assert.Equal(DimensionLifetime.Persistent, request().Lifetime);
    }

    [Fact]
    public void Create_Should_Pass_Through_Ephemeral_Choice()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).Ephemeral().Create();

        Assert.Equal(DimensionLifetime.Ephemeral, request().Lifetime);
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
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).WithGenerationRadius(5).RegisterStatic();

        Assert.Equal(5, request().GenerationRadius);
    }

    [Fact]
    public void WithGenerationRadius_Should_Default_To_DefaultGenerationRadius()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.Equal(DimensionBuilderImpl.DefaultGenerationRadius, request().GenerationRadius);
    }

    [Fact]
    public void WithGenerationRadius_Should_Throw_When_Out_Of_Range()
    {
        var b1 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => b1.WithGenerationRadius(-1));

        var b2 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => b2.WithGenerationRadius(17));
    }

    [Fact]
    public void WithGenerationRadius_Should_Accept_Boundary_Values()
    {
        var (b0, request0) = Capturing("mod:a");
        b0.WithWorldgen(new FakeWorldgenStrategy()).WithGenerationRadius(0).RegisterStatic();
        Assert.Equal(0, request0().GenerationRadius);

        var (b16, request16) = Capturing("mod:b");
        b16.WithWorldgen(new FakeWorldgenStrategy()).WithGenerationRadius(16).RegisterStatic();
        Assert.Equal(16, request16().GenerationRadius);
    }

    [Fact]
    public void WithSpawnBehavior_Should_Pass_Through_To_Request()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithSpawnBehavior(Manifold.Api.Transitions.SpawnBehavior.LastVisited)
            .RegisterStatic();

        Assert.Equal(Manifold.Api.Transitions.SpawnBehavior.LastVisited, request().SpawnBehavior);
    }

    [Fact]
    public void WithFixedSpawn_Should_Set_DimensionSpawn_And_Point()
    {
        var (builder, request) = Capturing();

        var spawn = new Vintagestory.API.MathTools.BlockPos(10, 64, 20, 0);
        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithFixedSpawn(spawn)
            .RegisterStatic();

        Assert.Equal(Manifold.Api.Transitions.SpawnBehavior.DimensionSpawn, request().SpawnBehavior);
        Assert.Equal(spawn, request().SpawnPoint);
    }

    [Fact]
    public void WithForcedGameMode_Should_Pass_Through_To_Request()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithForcedGameMode(EnumGameMode.Creative)
            .RegisterStatic();

        Assert.Equal(EnumGameMode.Creative, request().ForcedGameMode);
    }

    [Fact]
    public void SpawnBehavior_Should_Default_To_SameCoordinates_And_GameMode_Null()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.Equal(Manifold.Api.Transitions.SpawnBehavior.SameCoordinates, request().SpawnBehavior);
        Assert.Null(request().SpawnPoint);
        Assert.Null(request().ForcedGameMode);
    }

    [Fact]
    public void Streaming_Should_Set_StreamingLoadRadius_On_Request()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).Streaming(5).RegisterStatic();

        Assert.Equal(5, request().StreamingLoadRadius);
    }

    [Fact]
    public void StreamingLoadRadius_Should_Default_To_Null()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.Null(request().StreamingLoadRadius);
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
    public void WithRelightHeight_Should_Throw_When_Out_Of_Range()
    {
        var b1 = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
#pragma warning disable CS0618
        Assert.Throws<ArgumentOutOfRangeException>(() => b1.WithRelightHeight(0));
#pragma warning restore CS0618
    }

    [Fact]
    public void WithSeparateInventory_Should_Flow_To_Request()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy())
            .WithSeparateInventory(Manifold.Api.ManifoldInventory.Hotbar | Manifold.Api.ManifoldInventory.Backpack)
            .RegisterStatic();

        Assert.Equal(
            Manifold.Api.ManifoldInventory.Hotbar | Manifold.Api.ManifoldInventory.Backpack,
            request().SeparateInventory);
    }

    [Fact]
    public void SeparateInventory_Should_Default_To_None()
    {
        var (builder, request) = Capturing();

        builder.WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();

        Assert.Equal(Manifold.Api.ManifoldInventory.None, request().SeparateInventory);
    }

    /// <summary>
    /// A builder whose completion callback captures the <see cref="DimensionBuildRequest"/> it was
    /// given, for the many tests that only check what a builder method put on the request.
    /// </summary>
    private static (DimensionBuilderImpl Builder, Func<DimensionBuildRequest> Request) Capturing(string code = "mod:a")
    {
        DimensionBuildRequest? captured = null;
        var builder = new DimensionBuilderImpl(Code(code), "mod", req =>
        {
            captured = req;
            return Substitute.For<IDimension>();
        });
        return (builder, () => captured ?? throw new InvalidOperationException("Request not captured: builder was never completed."));
    }

    private static AssetLocation Code(string s) => new(s);
}
