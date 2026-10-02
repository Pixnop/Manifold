using System.Collections.Generic;
using Manifold.Internal;
using Vintagestory.API.MathTools;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class BlockLightRestorerTests
{
    private static readonly BlockPos Min = new(0, 0, 0, 0);
    private static readonly BlockPos Max = new(31, 31, 31, 0);

    [Fact]
    public void Relight_Should_Stamp_The_Dimension_On_Both_Corners()
    {
        var engine = new FakeEngine();
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(7, engine.RelitMin!.dimension);
        Assert.Equal(7, engine.RelitMax!.dimension);
        Assert.Equal(0, Min.dimension);
    }

    [Fact]
    public void Relight_Should_Return_False_And_Do_Nothing_Else_When_FullRelight_Fails()
    {
        var engine = new FakeEngine { FullRelightSucceeds = false, Sources = { new BlockPos(1, 1, 1, 7) } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        Assert.False(restorer.Relight(7, Min, Max, sendToClients: true));

        Assert.Empty(engine.Queued);
        Assert.Equal(0, engine.Broadcasts);
        Assert.Equal(0, restorer.PendingSourceCount);
    }

    [Fact]
    public void Relight_Should_Broadcast_At_Once_When_There_Is_No_Light_Source()
    {
        var engine = new FakeEngine();
        var restorer = new BlockLightRestorer(engine, () => 0);

        Assert.True(restorer.Relight(7, Min, Max, sendToClients: true));

        Assert.Equal(1, engine.Broadcasts);
        restorer.Tick();
        Assert.Equal(1, engine.Broadcasts);
    }

    [Fact]
    public void Relight_Should_Not_Broadcast_When_Clients_Are_Not_Wanted()
    {
        var engine = new FakeEngine();
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: false);

        Assert.Equal(0, engine.Broadcasts);
    }

    [Fact]
    public void Relight_Should_Queue_Sources_At_Once_When_The_Gate_Is_Open()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(new[] { source }, engine.Queued);
        Assert.Equal(0, engine.Broadcasts);
        Assert.Equal(1, restorer.PendingSourceCount);
    }

    [Fact]
    public void Relight_Should_Queue_A_Source_That_Already_Reads_As_Lit()
    {
        // A source just outside the cleared chunks keeps its own light but must be recomputed for
        // what it shines into them.
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source }, Lit = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(new[] { source }, engine.Queued);
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Equal(0, engine.Broadcasts);
        restorer.Tick();
        Assert.Equal(1, engine.Broadcasts);
    }

    [Fact]
    public void Tick_Should_Broadcast_One_Pass_After_Every_Source_Is_Lit()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);
        restorer.Relight(7, Min, Max, sendToClients: true);

        restorer.Tick();
        Assert.Equal(0, engine.Broadcasts);

        engine.Lit.Add(source);
        restorer.Tick();
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Equal(0, engine.Broadcasts);

        restorer.Tick();
        Assert.Equal(1, engine.Broadcasts);

        restorer.Tick();
        Assert.Equal(1, engine.Broadcasts);
        Assert.Single(engine.Queued);
    }

    [Fact]
    public void Tick_Should_Keep_A_Source_Pending_And_Queue_It_Once_When_The_Gate_Opens_Later()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source }, GateOpen = false };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);
        restorer.Tick();
        Assert.Empty(engine.Queued);
        Assert.Equal(1, restorer.PendingSourceCount);

        engine.GateOpen = true;
        restorer.Tick();
        restorer.Tick();

        Assert.Single(engine.Queued);
        Assert.Equal(1, restorer.PendingSourceCount);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Tick_Should_Drop_A_Candidate_That_Is_Not_A_Light_Source()
    {
        var engine = new FakeEngine { Sources = { new BlockPos(1, 1, 1, 7) }, IsSource = false };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(0, restorer.PendingSourceCount);
        restorer.Tick();
        Assert.Equal(1, engine.Broadcasts);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Tick_Should_Warn_Once_Broadcast_And_Drop_When_The_Timeout_Passes()
    {
        long now = 1000;
        var engine = new FakeEngine { Sources = { new BlockPos(1, 1, 1, 7), new BlockPos(2, 1, 1, 7) }, GateOpen = false };
        var restorer = new BlockLightRestorer(engine, () => now);
        restorer.Relight(7, Min, Max, sendToClients: true);

        now = 1000 + BlockLightRestorer.TimeoutMs - 1;
        restorer.Tick();
        Assert.Empty(engine.Warnings);
        Assert.Equal(2, restorer.PendingSourceCount);

        now = 1000 + BlockLightRestorer.TimeoutMs;
        restorer.Tick();
        restorer.Tick();

        string warning = Assert.Single(engine.Warnings);
        Assert.Contains("2 light source(s)", warning);
        Assert.Equal(1, engine.Broadcasts);
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Empty(engine.Queued);
    }

    [Fact]
    public void ForgetDimension_Should_Drop_Only_That_Dimensions_Pending_Sources()
    {
        var engine = new FakeEngine { GateOpen = false };
        var restorer = new BlockLightRestorer(engine, () => 0);
        engine.Sources.Add(new BlockPos(1, 1, 1, 7));
        restorer.Relight(7, Min, Max, sendToClients: true);
        engine.Sources[0] = new BlockPos(1, 1, 1, 8);
        restorer.Relight(8, Min, Max, sendToClients: true);

        restorer.ForgetDimension(7);

        Assert.Equal(1, restorer.PendingSourceCount);
        engine.GateOpen = true;
        restorer.Tick();
        Assert.Equal(8, Assert.Single(engine.Queued).dimension);
        Assert.Equal(0, engine.Broadcasts);
    }

    private sealed class FakeEngine : IRelightEngine
    {
        public bool FullRelightSucceeds { get; set; } = true;

        public bool GateOpen { get; set; } = true;

        public bool IsSource { get; set; } = true;

        public List<BlockPos> Sources { get; } = new();

        public HashSet<BlockPos> Lit { get; } = new();

        public List<BlockPos> Queued { get; } = new();

        public List<string> Warnings { get; } = new();

        public int Broadcasts { get; private set; }

        public BlockPos? RelitMin { get; private set; }

        public BlockPos? RelitMax { get; private set; }

        public bool FullRelight(BlockPos min, BlockPos max)
        {
            RelitMin = min;
            RelitMax = max;
            return FullRelightSucceeds;
        }

        public IReadOnlyList<BlockPos> FindLightSources(BlockPos min, BlockPos max) => new List<BlockPos>(Sources);

        public bool IsGateOpen(BlockPos pos) => GateOpen;

        public bool QueueBlockLight(BlockPos pos)
        {
            if (IsSource)
            {
                Queued.Add(pos);
            }

            return IsSource;
        }

        public bool IsLit(BlockPos pos) => Lit.Contains(pos);

        public void Broadcast(BlockPos min, BlockPos max) => Broadcasts++;

        public void Warn(string message) => Warnings.Add(message);
    }
}
