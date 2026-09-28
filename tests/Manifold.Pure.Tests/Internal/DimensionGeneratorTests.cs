using System;
using Manifold.Api;
using Manifold.Api.Worldgen;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
        var generator = NewGenerator();
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
        var generator = NewGenerator();
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
        var generator = NewGenerator();
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
        var generator = NewGenerator();
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
        var generator = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        generator.InvokeStrategyColumn(throwing, ctx, 42);
        generator.InvokeStrategyColumn(throwing, ctx, 42);
        Assert.Equal(2, generator.GetConsecutiveFailureCount(42));

        // Now invoke with a good strategy - resets the counter.
        generator.InvokeStrategyColumn(new FakeWorldgenStrategy(), ctx, 42);

        Assert.Equal(0, generator.GetConsecutiveFailureCount(42));
    }

    /// <summary>
    /// A disabled dimension is silently skipped; no further invocations fire.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_Skip_When_Disabled()
    {
        var generator = NewGenerator();
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

    /// <summary>
    /// <see cref="DimensionGenerator.ForgetDimension"/> drops the disabled/failure state, so a
    /// recycled engine id does not inherit a previous dimension's auto-disabled strategy.
    /// </summary>
    [Fact]
    public void ForgetDimension_Should_Reset_Disabled_And_Failure_State()
    {
        var generator = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);
        for (int i = 0; i < 4; i++)
        {
            generator.InvokeStrategyColumn(throwing, ctx, 42);
        }

        Assert.True(generator.IsDisabled(42));

        generator.ForgetDimension(42);

        Assert.False(generator.IsDisabled(42));
        Assert.Equal(0, generator.GetConsecutiveFailureCount(42));
    }

    /// <summary>
    /// Three consecutive throws stay under <c>MaxConsecutiveFailures</c> (4) and must not disable.
    /// </summary>
    [Fact]
    public void InvokeStrategyColumn_Should_Not_AutoDisable_After_Three_Consecutive_Throws()
    {
        var generator = NewGenerator();
        var throwing = new ThrowingStrategy();
        var ctx = FakeCtx(42);

        for (int i = 0; i < 3; i++)
        {
            generator.InvokeStrategyColumn(throwing, ctx, 42);
        }

        Assert.False(generator.IsDisabled(42));
    }

    /// <summary>
    /// A failed <see cref="IWorldgenStrategy.OnInitialize"/> must not generate uninitialized terrain:
    /// the init marker is removed so the next visit retries instead of skipping straight to
    /// <see cref="DimensionGenerator.GetConsecutiveFailureCount"/>'s target of failing forever.
    /// </summary>
    [Fact]
    public void EnsureColumn_Should_Retry_OnInitialize_After_A_Failure()
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        var strategy = new RecordingWorldgenStrategy { ThrowOnNextInitialize = true };
        var dim = registry.DefineForOwner(Code("owner:retry"), "owner")
            .WithWorldgen(strategy)
            .RegisterStatic();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var sapi = Substitute.For<ICoreServerAPI>();

        bool first = generator.EnsureColumn(sapi, dim.InternalId, 0, 0);
        Assert.False(first);
        Assert.Equal(1, strategy.InitCallCount);

        bool second = generator.EnsureColumn(sapi, dim.InternalId, 0, 0);
        Assert.True(second);
        Assert.Equal(2, strategy.InitCallCount);
        sapi.WorldManager.Received(1).CreateChunkColumnForDimension(0, 0, dim.InternalId);
    }

    /// <summary>
    /// <see cref="Manifold.Api.Server.IDimensionRegistry.ColumnGenerated"/> fires the first time a
    /// column is generated.
    /// </summary>
    [Fact]
    public void EnsureColumn_Should_Fire_ColumnGenerated_On_First_Generation()
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        var dim = registry.DefineForOwner(Code("owner:colgen"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var sapi = Substitute.For<ICoreServerAPI>();

        int fired = 0;
        Manifold.Api.Events.ColumnGeneratedEventArgs? args = null;
        registry.ColumnGenerated += (_, e) =>
        {
            fired++;
            args = e;
        };

        bool generated = generator.EnsureColumn(sapi, dim.InternalId, 3, 4);

        Assert.True(generated);
        Assert.Equal(1, fired);
        Assert.NotNull(args);
        Assert.Equal(dim.InternalId, args!.Dimension.InternalId);
        Assert.Equal(3, args.ChunkX);
        Assert.Equal(4, args.ChunkZ);
    }

    /// <summary>
    /// Re-visiting an already generated column loads it and must not raise
    /// <see cref="Manifold.Api.Server.IDimensionRegistry.ColumnGenerated"/> again.
    /// </summary>
    [Fact]
    public void EnsureColumn_Should_Not_Fire_ColumnGenerated_On_Load()
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        var dim = registry.DefineForOwner(Code("owner:colgen2"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var sapi = Substitute.For<ICoreServerAPI>();

        Assert.True(generator.EnsureColumn(sapi, dim.InternalId, 0, 0));

        int fired = 0;
        registry.ColumnGenerated += (_, _) => fired++;

        bool generatedAgain = generator.EnsureColumn(sapi, dim.InternalId, 0, 0);

        Assert.False(generatedAgain);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void RelightBlockBounds_Should_Return_True_On_Success()
    {
        var sapi = Substitute.For<ICoreServerAPI>();
        bool ok = DimensionGenerator.RelightBlockBounds(
            sapi, 5, new BlockPos(0, 0, 0, 0), new BlockPos(31, 64, 31, 0), sendToClients: true);
        Assert.True(ok);
    }

    [Fact]
    public void RelightBlockBounds_Should_Return_False_And_Log_A_Warning_When_FullRelight_Throws()
    {
        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.WorldManager
            .When(w => w.FullRelight(Arg.Any<BlockPos>(), Arg.Any<BlockPos>(), Arg.Any<bool>()))
            .Do(_ => throw new InvalidOperationException("chunk not loaded"));

        // Best-effort: the caller (the /manifold relight command) must learn about the failure
        // through the return value, not an escaping exception.
        bool ok = DimensionGenerator.RelightBlockBounds(
            sapi, 5, new BlockPos(0, 0, 0, 0), new BlockPos(31, 64, 31, 0), sendToClients: true);

        Assert.False(ok);
        sapi.Logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    private static DimensionGenerator NewGenerator() =>
        new(new DimensionRegistry(new DimensionAllocator()), new GeneratedColumnStore());

    private static Vintagestory.API.Common.AssetLocation Code(string s) => new(s);

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
