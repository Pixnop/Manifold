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
    public void WithWorldgen_Should_Throw_When_Strategy_Has_Empty_Passes()
    {
        var builder = new DimensionBuilderImpl(Code("mod:a"), "mod", _ => throw new InvalidOperationException());
        var bad = new FakeWorldgenStrategy { Passes = new System.Collections.Generic.HashSet<Vintagestory.API.Server.EnumWorldGenPass>() };
        Assert.Throws<WorldgenStrategyContractException>(() => builder.WithWorldgen(bad));
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

    private static AssetLocation Code(string s) => new(s);
}
