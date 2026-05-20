using System;
using Manifold.Api;
using Manifold.Api.Worldgen;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.MathTools;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// Tests for <see cref="DimensionGenerator"/> failure-handling seam via
/// <see cref="DimensionGenerator.InvokeStrategyColumn"/>.
/// </summary>
public sealed class DimensionGeneratorTests
{
    /// <summary>
    /// A single throw increments the failure counter to 1.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_Increment_Failure_Count_On_Throw()
    {
        var (generator, _) = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        generator.InvokeStrategyColumn(throwing, ctx, 42);

        Assert.Equal(1, generator.GetConsecutiveFailureCount(42));
    }

    /// <summary>
    /// <see cref="DimensionGenerator.StrategyThrew"/> fires when a strategy throws.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_Fire_StrategyThrew_On_Exception()
    {
        var (generator, _) = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        int eventCount = 0;
        generator.StrategyThrew += (_, _, _) => eventCount++;

        generator.InvokeStrategyColumn(throwing, ctx, 42);

        Assert.Equal(1, eventCount);
    }

    /// <summary>
    /// Four consecutive throws auto-disable the dimension.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_AutoDisable_After_Four_Consecutive_Throws()
    {
        var (generator, _) = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        for (int i = 0; i < 4; i++)
        {
            generator.InvokeStrategyColumn(throwing, ctx, 42);
        }

        Assert.True(generator.IsDisabled(42));
    }

    /// <summary>
    /// <see cref="DimensionGenerator.StrategyAutoDisabled"/> fires exactly once on disable.
    /// </summary>
    [Fact]
    public void StrategyAutoDisabled_Should_Fire_Once_When_Disabled()
    {
        var (generator, _) = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        int eventCount = 0;
        generator.StrategyAutoDisabled += _ => eventCount++;

        for (int i = 0; i < 6; i++)
        {
            generator.InvokeStrategyColumn(throwing, ctx, 42);
        }

        Assert.Equal(1, eventCount);
    }

    /// <summary>
    /// A successful invocation resets the consecutive failure counter to zero.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_Reset_Failure_Count_On_Success()
    {
        var (generator, _) = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        generator.InvokeStrategyColumn(throwing, ctx, 42);
        generator.InvokeStrategyColumn(throwing, ctx, 42);
        Assert.Equal(2, generator.GetConsecutiveFailureCount(42));

        // Now invoke with a good strategy — resets the counter.
        generator.InvokeStrategyColumn(new FakeWorldgenStrategy(), ctx, 42);

        Assert.Equal(0, generator.GetConsecutiveFailureCount(42));
    }

    /// <summary>
    /// A disabled dimension is silently skipped; no further invocations fire.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_Skip_When_Disabled()
    {
        var (generator, _) = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        // Reach auto-disable.
        for (int i = 0; i < 4; i++)
        {
            generator.InvokeStrategyColumn(throwing, ctx, 42);
        }

        Assert.True(generator.IsDisabled(42));

        int callsBefore = throwing.CallCount;
        generator.InvokeStrategyColumn(throwing, ctx, 42);

        Assert.Equal(callsBefore, throwing.CallCount);
    }

    private static (DimensionGenerator Generator, DimensionRegistry Registry) NewGenerator()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        return (new DimensionGenerator(registry, new GeneratedColumnStore()), registry);
    }

    private static IWorldgenChunkContext FakeCtx(int dim)
    {
        var ctx = Substitute.For<IWorldgenChunkContext>();
        ctx.DimensionId.Returns(dim);
        ctx.ChunkX.Returns(0);
        ctx.ChunkZ.Returns(0);
        ctx.Rng.Returns(new LCGRandom(0));
        return ctx;
    }

    private sealed class ThrowingStrategy : IWorldgenStrategy
    {
        /// <summary>Number of times <see cref="GenerateColumn"/> has been invoked.</summary>
        public int CallCount { get; private set; }

        /// <inheritdoc/>
        public void OnInitialize(IWorldgenInitContext ctx)
        {
        }

        /// <inheritdoc/>
        public void GenerateColumn(IWorldgenChunkContext ctx)
        {
            CallCount++;
            throw new InvalidOperationException("boom");
        }
    }
}
