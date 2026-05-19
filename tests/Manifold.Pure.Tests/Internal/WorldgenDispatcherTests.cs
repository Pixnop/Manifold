using System.Collections.Generic;
using Manifold.Api.Worldgen;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class WorldgenDispatcherTests
{
    [Fact]
    public void Dispatch_Should_NoOp_When_No_Strategy_Bound_For_Dim()
    {
        var dispatcher = new WorldgenDispatcher();
        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);

        // No exception means success.
    }

    [Fact]
    public void Dispatch_Should_Invoke_Bound_Strategy_For_Matching_Dim_And_Pass()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new RecordingWorldgenStrategy();
        dispatcher.Bind(42, strat);

        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);

        Assert.Single(strat.ChunkGenCalls);
        Assert.Equal((42, EnumWorldGenPass.PreDone), strat.ChunkGenCalls[0]);
    }

    [Fact]
    public void Dispatch_Should_Skip_Strategy_When_Pass_Not_Declared()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new RecordingWorldgenStrategy
        {
            Passes = new HashSet<EnumWorldGenPass> { EnumWorldGenPass.PreDone },
        };
        dispatcher.Bind(42, strat);

        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.Terrain);

        Assert.Empty(strat.ChunkGenCalls);
    }

    [Fact]
    public void Unbind_Should_Detach_Strategy()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new RecordingWorldgenStrategy();
        dispatcher.Bind(42, strat);
        dispatcher.Unbind(42);

        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);
        Assert.Empty(strat.ChunkGenCalls);
    }

    [Fact]
    public void Bind_Should_Replace_Existing_Strategy_For_Same_Dim()
    {
        var dispatcher = new WorldgenDispatcher();
        var first = new RecordingWorldgenStrategy();
        var second = new RecordingWorldgenStrategy();
        dispatcher.Bind(42, first);
        dispatcher.Bind(42, second);

        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);

        Assert.Empty(first.ChunkGenCalls);
        Assert.Single(second.ChunkGenCalls);
    }

    [Fact]
    public void Dispatch_Should_Swallow_And_Count_Strategy_Throw()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new ThrowingStrategy();
        dispatcher.Bind(42, strat);

        // Must not propagate.
        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);

        Assert.Equal(1, dispatcher.GetConsecutiveFailureCount(42));
    }

    [Fact]
    public void Dispatch_Should_Reset_Failure_Counter_On_Success()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new ThrowingStrategy();
        dispatcher.Bind(42, strat);

        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone); // fail #1
        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone); // fail #2

        var good = new RecordingWorldgenStrategy();
        dispatcher.Bind(42, good); // also resets counter
        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);

        Assert.Equal(0, dispatcher.GetConsecutiveFailureCount(42));
    }

    [Fact]
    public void Dispatch_Should_Auto_Disable_Strategy_After_4_Consecutive_Throws()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new ThrowingStrategy();
        dispatcher.Bind(42, strat);

        for (int i = 0; i < 4; i++)
        {
            dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);
        }

        Assert.True(dispatcher.IsDisabled(42));

        int beforeCalls = strat.CallCount;
        dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);
        Assert.Equal(beforeCalls, strat.CallCount);
    }

    [Fact]
    public void StrategyAutoDisabled_Event_Should_Fire_Once_On_Disable()
    {
        var dispatcher = new WorldgenDispatcher();
        var strat = new ThrowingStrategy();
        dispatcher.Bind(42, strat);

        int eventCount = 0;
        dispatcher.StrategyAutoDisabled += _ => eventCount++;

        for (int i = 0; i < 6; i++)
        {
            dispatcher.Dispatch(NewCtx(42), EnumWorldGenPass.PreDone);
        }

        Assert.Equal(1, eventCount);
    }

    private static IWorldgenChunkContext NewCtx(int dim)
    {
        var ctx = Substitute.For<IWorldgenChunkContext>();
        ctx.DimensionId.Returns(dim);
        return ctx;
    }

    private sealed class ThrowingStrategy : IWorldgenStrategy
    {
        public IReadOnlySet<EnumWorldGenPass> Passes { get; } =
            new HashSet<EnumWorldGenPass> { EnumWorldGenPass.PreDone };

        public int CallCount { get; private set; }

        public void OnInitialize(IWorldgenInitContext ctx)
        {
        }

        public void OnChunkColumnGen(IWorldgenChunkContext ctx, EnumWorldGenPass pass)
        {
            CallCount++;
            throw new System.Exception("boom");
        }
    }
}
