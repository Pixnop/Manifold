using System;
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
    public void Relight_Should_Still_Restore_And_Resend_When_FullRelight_Fails()
    {
        // FullRelight clears the light before anything in it can throw: giving up there would
        // leave the area dark for good.
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { FullRelightSucceeds = false, Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        Assert.False(restorer.Relight(7, Min, Max, sendToClients: true));

        Assert.Equal(new[] { source }, engine.Queued);
        engine.Lit.Add(source);
        restorer.Tick();
        restorer.Tick();
        Assert.Single(engine.Resends);
    }

    [Fact]
    public void Relight_Should_Resend_At_Once_When_There_Is_No_Light_Source()
    {
        var engine = new FakeEngine();
        var restorer = new BlockLightRestorer(engine, () => 0);

        Assert.True(restorer.Relight(7, Min, Max, sendToClients: true));

        (int dim, int chunks) = Assert.Single(engine.Resends);
        Assert.Equal(7, dim);
        Assert.Equal(1, chunks);
        restorer.Tick();
        Assert.Single(engine.Resends);
    }

    [Fact]
    public void Relight_Should_Not_Resend_When_Clients_Are_Not_Wanted()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: false);
        engine.Lit.Add(source);
        restorer.Tick();
        restorer.Tick();
        restorer.Tick();

        Assert.Empty(engine.Resends);
        Assert.Equal(0, restorer.PendingSourceCount);
    }

    [Fact]
    public void Relight_Should_Queue_Sources_At_Once_When_The_Gate_Is_Open()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(new[] { source }, engine.Queued);
        Assert.Empty(engine.Resends);
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
        Assert.Empty(engine.Resends);
        restorer.Tick();
        Assert.Equal(0, restorer.PendingSourceCount);
        restorer.Tick();
        Assert.Single(engine.Resends);
    }

    [Fact]
    public void Tick_Should_Resend_One_Pass_After_Every_Queued_Source_Is_Lit()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);
        restorer.Relight(7, Min, Max, sendToClients: true);

        restorer.Tick();
        Assert.Empty(engine.Resends);

        engine.Lit.Add(source);
        restorer.Tick();
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Empty(engine.Resends);

        restorer.Tick();
        Assert.Single(engine.Resends);

        restorer.Tick();
        Assert.Single(engine.Resends);
        Assert.Single(engine.Queued);
    }

    [Fact]
    public void Tick_Should_Resend_Without_Waiting_For_Sources_Behind_A_Closed_Gate()
    {
        // Mixed open and closed gate in one request: a pocket larger than what the engine keeps
        // loaded around the player. The near lights must reach clients now, not at the timeout.
        var near = new BlockPos(1, 1, 1, 7);
        var far = new BlockPos(320, 1, 1, 7);
        var engine = new FakeEngine { Sources = { near, far }, ClosedColumns = { (10, 0) } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);
        Assert.Equal(new[] { near }, engine.Queued);

        engine.Lit.Add(near);
        restorer.Tick();
        restorer.Tick();
        Assert.Single(engine.Resends);
        Assert.Equal(1, restorer.PendingSourceCount);

        // The far column opens later: its source is queued, and its chunks are resent in turn.
        engine.ClosedColumns.Clear();
        restorer.Tick();
        Assert.Equal(new[] { near, far }, engine.Queued);
        engine.Lit.Add(far);
        restorer.Tick();
        restorer.Tick();

        Assert.Equal(2, engine.Resends.Count);
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Tick_Should_Test_The_Gate_Once_Per_Column_Not_Once_Per_Source()
    {
        var engine = new FakeEngine { ClosedColumns = { (0, 0) } };
        for (int y = 0; y < 20; y++)
        {
            engine.Sources.Add(new BlockPos(1, y, 1, 7));
        }

        var restorer = new BlockLightRestorer(engine, () => 0);
        restorer.Relight(7, Min, Max, sendToClients: true);
        int afterRelight = engine.GateTests;

        restorer.Tick();

        Assert.Equal(1, afterRelight);
        Assert.Equal(2, engine.GateTests);
        Assert.Equal(20, restorer.PendingSourceCount);
    }

    [Fact]
    public void Relight_Should_Keep_One_Pending_Entry_Per_Position_When_Requests_Overlap()
    {
        // What a per-column caller does: every request lists the same neighbourhood again.
        var a = new BlockPos(1, 1, 1, 7);
        var b = new BlockPos(40, 1, 1, 7);
        var engine = new FakeEngine { Sources = { a, b }, ClosedColumns = { (0, 0), (1, 0) } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);
        restorer.Relight(7, Min, Max, sendToClients: true);
        restorer.Relight(7, new BlockPos(32, 0, 0, 0), new BlockPos(63, 31, 31, 0), sendToClients: true);

        Assert.Equal(2, restorer.PendingSourceCount);

        engine.ClosedColumns.Clear();
        restorer.Tick();

        Assert.Equal(2, engine.Queued.Count);
    }

    [Fact]
    public void Relight_Should_Queue_Again_A_Source_A_Later_Request_Cleared_Again()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);
        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(new[] { source, source }, engine.Queued);
        Assert.Equal(1, restorer.PendingSourceCount);
    }

    [Fact]
    public void Relight_Should_Refresh_The_Timeout_Of_A_Source_Listed_Again()
    {
        long now = 0;
        var engine = new FakeEngine { Sources = { new BlockPos(1, 1, 1, 7) }, ClosedColumns = { (0, 0) } };
        var restorer = new BlockLightRestorer(engine, () => now);
        restorer.Relight(7, Min, Max, sendToClients: true);

        now = BlockLightRestorer.TimeoutMs - 1;
        restorer.Relight(7, Min, Max, sendToClients: true);
        now = BlockLightRestorer.TimeoutMs + 1;
        restorer.Tick();

        Assert.Equal(1, restorer.PendingSourceCount);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Relight_Should_Stop_At_The_Cap_And_Warn_Once()
    {
        var engine = new FakeEngine { ClosedColumns = { (0, 0) } };
        var restorer = new BlockLightRestorer(engine, () => 0);
        for (int i = 0; i < BlockLightRestorer.MaxPendingSources + 5; i++)
        {
            engine.Sources.Add(new BlockPos(i % 32, i / 1024, (i / 32) % 32, 7));
        }

        restorer.Relight(7, Min, Max, sendToClients: false);
        restorer.Relight(7, Min, Max, sendToClients: false);

        Assert.Equal(BlockLightRestorer.MaxPendingSources, restorer.PendingSourceCount);
        string warning = Assert.Single(engine.Warnings);
        Assert.Contains(BlockLightRestorer.MaxPendingSources.ToString(System.Globalization.CultureInfo.InvariantCulture), warning);
    }

    [Fact]
    public void Tick_Should_Keep_A_Source_Pending_And_Queue_It_Once_When_The_Gate_Opens_Later()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source }, ClosedColumns = { (0, 0) } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);
        restorer.Tick();
        Assert.Empty(engine.Queued);
        Assert.Equal(1, restorer.PendingSourceCount);

        engine.ClosedColumns.Clear();
        restorer.Tick();
        restorer.Tick();

        Assert.Single(engine.Queued);
        Assert.Equal(1, restorer.PendingSourceCount);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Tick_Should_Queue_Again_When_The_Gate_Closed_Before_The_Engine_Lit_The_Source()
    {
        var source = new BlockPos(1, 1, 1, 7);
        var engine = new FakeEngine { Sources = { source } };
        var restorer = new BlockLightRestorer(engine, () => 0);
        restorer.Relight(7, Min, Max, sendToClients: true);

        engine.ClosedColumns.Add((0, 0));
        restorer.Tick();
        engine.ClosedColumns.Clear();
        restorer.Tick();

        Assert.Equal(new[] { source, source }, engine.Queued);
    }

    [Fact]
    public void Tick_Should_Drop_A_Candidate_That_Is_Not_A_Light_Source()
    {
        var engine = new FakeEngine { Sources = { new BlockPos(1, 1, 1, 7) }, IsSource = false };
        var restorer = new BlockLightRestorer(engine, () => 0);

        restorer.Relight(7, Min, Max, sendToClients: true);

        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Single(engine.Resends);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Tick_Should_Settle_A_Queued_Source_Whose_Block_Was_Removed_Before_It_Was_Lit()
    {
        // A torch broken (or burnt out) between queueing and lighting never reads as lit.
        var engine = new FakeEngine { Sources = { new BlockPos(1, 1, 1, 7) } };
        var restorer = new BlockLightRestorer(engine, () => 0);
        restorer.Relight(7, Min, Max, sendToClients: true);

        engine.IsSource = false;
        restorer.Tick();
        restorer.Tick();

        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Single(engine.Resends);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void Tick_Should_Warn_Once_And_Drop_When_The_Timeout_Passes()
    {
        long now = 1000;
        var engine = new FakeEngine
        {
            Sources = { new BlockPos(1, 1, 1, 7), new BlockPos(40, 1, 1, 7) },
            ClosedColumns = { (0, 0), (1, 0) },
        };
        var restorer = new BlockLightRestorer(engine, () => now);
        restorer.Relight(7, Min, Max, sendToClients: true);

        // The sunlight was resent at once: nothing was being computed.
        Assert.Single(engine.Resends);

        now = 1000 + BlockLightRestorer.TimeoutMs - 1;
        restorer.Tick();
        Assert.Empty(engine.Warnings);
        Assert.Equal(2, restorer.PendingSourceCount);

        now = 1000 + BlockLightRestorer.TimeoutMs;
        restorer.Tick();
        restorer.Tick();

        string warning = Assert.Single(engine.Warnings);
        Assert.Contains("2 light source(s)", warning);
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Empty(engine.Queued);
        Assert.Single(engine.Resends);
    }

    [Fact]
    public void Tick_Should_Skip_A_Source_That_Throws_And_Carry_On_With_The_Others()
    {
        var bad = new BlockPos(1, 1, 1, 7);
        var good = new BlockPos(2, 1, 1, 7);
        var engine = new FakeEngine { Sources = { bad, good }, Throwing = { bad } };
        var restorer = new BlockLightRestorer(engine, () => 0);

        Assert.True(restorer.Relight(7, Min, Max, sendToClients: true));
        engine.Lit.Add(good);
        restorer.Tick();
        restorer.Tick();
        restorer.Tick();

        Assert.Equal(new[] { good }, engine.Queued);
        Assert.Equal(0, restorer.PendingSourceCount);
        Assert.Single(engine.Resends);
        Assert.Contains("failed", Assert.Single(engine.Warnings));
    }

    [Fact]
    public void Relight_Should_Not_Throw_When_The_Scan_Or_The_Resend_Throws()
    {
        var engine = new FakeEngine { ScanThrows = true, ResendThrows = true };
        var restorer = new BlockLightRestorer(engine, () => 0);

        Assert.True(restorer.Relight(7, Min, Max, sendToClients: true));
        restorer.Tick();

        Assert.Single(engine.Warnings);
    }

    [Fact]
    public void ForgetDimension_Should_Drop_Only_That_Dimensions_Pending_Sources_And_Resends()
    {
        var engine = new FakeEngine { ClosedColumns = { (0, 0) } };
        var restorer = new BlockLightRestorer(engine, () => 0);
        engine.Sources.Add(new BlockPos(1, 1, 1, 7));
        restorer.Relight(7, Min, Max, sendToClients: false);
        engine.Sources[0] = new BlockPos(1, 1, 1, 8);
        restorer.Relight(8, Min, Max, sendToClients: false);

        restorer.ForgetDimension(7);

        Assert.Equal(1, restorer.PendingSourceCount);
        engine.ClosedColumns.Clear();
        restorer.Tick();
        Assert.Equal(8, Assert.Single(engine.Queued).dimension);
    }

    private sealed class FakeEngine : IRelightEngine
    {
        public bool FullRelightSucceeds { get; set; } = true;

        public bool IsSource { get; set; } = true;

        public bool ScanThrows { get; set; }

        public bool ResendThrows { get; set; }

        public HashSet<(int Cx, int Cz)> ClosedColumns { get; } = new();

        public List<BlockPos> Sources { get; } = new();

        public HashSet<BlockPos> Lit { get; } = new();

        public HashSet<BlockPos> Throwing { get; } = new();

        public List<BlockPos> Queued { get; } = new();

        public List<string> Warnings { get; } = new();

        public List<(int Dim, int Chunks)> Resends { get; } = new();

        public int GateTests { get; private set; }

        public BlockPos? RelitMin { get; private set; }

        public BlockPos? RelitMax { get; private set; }

        public bool FullRelight(BlockPos min, BlockPos max)
        {
            RelitMin = min;
            RelitMax = max;
            return FullRelightSucceeds;
        }

        public IReadOnlyList<BlockPos> FindLightSources(BlockPos min, BlockPos max) =>
            ScanThrows ? throw new InvalidOperationException("scan") : new List<BlockPos>(Sources);

        public bool IsGateOpen(int chunkX, int chunkZ)
        {
            GateTests++;
            return !ClosedColumns.Contains((chunkX, chunkZ));
        }

        public bool QueueBlockLight(BlockPos pos)
        {
            if (Throwing.Contains(pos))
            {
                throw new InvalidOperationException("third-party block code");
            }

            if (IsSource)
            {
                Queued.Add(pos);
            }

            return IsSource;
        }

        public bool EmitsLight(BlockPos pos) => IsSource;

        public bool IsLit(BlockPos pos) => Lit.Contains(pos);

        public void CollectAffectedChunks(BlockPos min, BlockPos max, ISet<(int Cx, int Cy, int Cz)> chunks) =>
            chunks.Add((min.X / 32, 0, min.Z / 32));

        public void Resend(int dimId, IReadOnlyCollection<(int Cx, int Cy, int Cz)> chunks)
        {
            if (ResendThrows)
            {
                throw new InvalidOperationException("resend");
            }

            Resends.Add((dimId, chunks.Count));
        }

        public void Warn(string message) => Warnings.Add(message);
    }
}
