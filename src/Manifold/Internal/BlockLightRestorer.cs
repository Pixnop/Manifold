using System;
using System.Collections.Generic;
using Manifold.Internal.Util;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Dimension-aware relight that keeps block light. The engine's <c>FullRelight</c> recomputes
/// sunlight correctly in a custom dimension but clears block light and puts it back at doubled
/// coordinates, so every light source outside chunk (0, 0, 0) ends up dark. After it runs, this
/// class hands each light source of the affected chunks back to the engine's own lighting queue
/// (the path a player placing a torch takes), which is asynchronous and silently drops work for a
/// column whose overworld map chunk is not loaded. Sources in such a column stay pending here, one
/// entry per position whatever the number of requests that listed it, and are retried from
/// <see cref="Tick"/> until the engine accepts them or <see cref="TimeoutMs"/> passes. Nothing is
/// persisted: a server restart forgets the pending sources.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class BlockLightRestorer
{
    /// <summary>
    /// How long a light source may wait for the engine to accept it. The engine opens a column once
    /// a player is near it in any dimension, which takes seconds after a transit (longer if the
    /// overworld terrain under the player still has to generate). Five minutes covers a relight
    /// requested just ahead of a player's arrival with a wide margin, and keeps a relight requested
    /// where nobody is from holding state and tick work for the rest of the session.
    /// </summary>
    internal const long TimeoutMs = 5 * 60 * 1000;

    /// <summary>
    /// Upper bound on the pending light sources, all dimensions together: about 10 MB of bookkeeping.
    /// Only a relight of a huge lit volume (a lava sea) with nobody near can reach it; what does
    /// not fit is not restored and stays dark until the next relight.
    /// </summary>
    internal const int MaxPendingSources = 100_000;

    /// <summary>A Y above any map height (the engine reserves 32768 blocks of Y per dimension).</summary>
    private const int ColumnTop = 32767;

    private readonly IRelightEngine _engine;
    private readonly Func<long> _clockMs;
    private readonly Dictionary<(int Dim, int Cx, int Cz), Column> _columns = new();
    private readonly Dictionary<int, Resend> _resends = new();
    private bool _capWarned;
    private bool _failureWarned;

    /// <summary>Initializes a new instance of the <see cref="BlockLightRestorer"/> class.</summary>
    /// <param name="engine">Engine operations.</param>
    /// <param name="clockMs">Monotonic clock in milliseconds.</param>
    public BlockLightRestorer(IRelightEngine engine, Func<long> clockMs)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _clockMs = clockMs ?? throw new ArgumentNullException(nameof(clockMs));
    }

    /// <summary>Gets the number of light sources still waiting for the engine to light them.</summary>
    public int PendingSourceCount { get; private set; }

    /// <summary>
    /// Relights the box in <paramref name="dimId"/>: sunlight synchronously, then block light
    /// through the engine's queue. The dimension of both corners is overwritten with
    /// <paramref name="dimId"/>, so a position built without one cannot relight the overworld.
    /// Never throws.
    /// </summary>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="min">Minimum corner (local coordinates).</param>
    /// <param name="max">Maximum corner (local coordinates).</param>
    /// <param name="sendToClients">
    /// Whether to resend the affected chunks to the players in that dimension once no light source
    /// is being computed (at once when there is none to compute).
    /// </param>
    /// <returns><c>false</c> if the engine's relight threw (logged as a warning).</returns>
    public bool Relight(int dimId, BlockPos min, BlockPos max, bool sendToClients)
    {
        var minPos = new BlockPos(min.X, min.Y, min.Z, dimId);
        var maxPos = new BlockPos(max.X, max.Y, max.Z, dimId);

        // Whatever FullRelight returns, carry on: it clears the light before anything in it can
        // throw, so stopping here would leave the area dark for good.
        bool relit = _engine.FullRelight(minPos, maxPos);
        try
        {
            AddSources(_engine.FindLightSources(minPos, maxPos), sendToClients);
            if (sendToClients)
            {
                _engine.CollectAffectedChunks(minPos, maxPos, ResendFor(dimId).Chunks);
            }
        }
        catch (Exception ex)
        {
            WarnFailureOnce("listing the light sources", ex);
        }

        Tick();
        return relit;
    }

    /// <summary>
    /// Moves the pending light sources forward and resends what is ready. Call regularly from a
    /// server tick listener. Never throws.
    /// </summary>
    public void Tick()
    {
        if (_columns.Count == 0 && _resends.Count == 0)
        {
            return;
        }

        long now = _clockMs();
        var busyDimensions = new HashSet<int>();
        var finished = new List<(int Dim, int Cx, int Cz)>();
        int expired = 0;
        foreach (var (key, column) in _columns)
        {
            AdvanceColumn(key, column, busyDimensions);
            if (column.Sources.Count > 0 && now >= column.DeadlineMs)
            {
                expired += column.Sources.Count;
                column.Sources.Clear();
            }

            if (column.Sources.Count == 0)
            {
                finished.Add(key);
            }
        }

        foreach (var key in finished)
        {
            _columns.Remove(key);
        }

        RecountPending();
        if (expired > 0)
        {
            _engine.Warn(
                $"[Manifold] Relight: {expired} light source(s) were still not lit after {TimeoutMs / 1000} s and were "
                + "dropped. The engine only lights a column whose overworld map chunk is loaded, which needs a player "
                + "near it; they stay dark until the next relight.");
        }

        FlushResends(busyDimensions);
    }

    /// <summary>Drops everything pending for a dimension that is being removed.</summary>
    /// <param name="dimId">Engine dimension id.</param>
    public void ForgetDimension(int dimId)
    {
        var keys = new List<(int Dim, int Cx, int Cz)>();
        foreach (var key in _columns.Keys)
        {
            if (key.Dim == dimId)
            {
                keys.Add(key);
            }
        }

        foreach (var key in keys)
        {
            _columns.Remove(key);
        }

        _resends.Remove(dimId);
        RecountPending();
    }

    private void AddSources(IReadOnlyList<BlockPos> sources, bool sendToClients)
    {
        long deadline = _clockMs() + TimeoutMs;
        foreach (var pos in sources)
        {
            var key = (pos.dimension, pos.X / ChunkMath.ChunkSize, pos.Z / ChunkMath.ChunkSize);
            if (!_columns.TryGetValue(key, out var column))
            {
                column = new Column();
                _columns[key] = column;
            }

            column.DeadlineMs = deadline;
            column.SendToClients |= sendToClients;

            // A position listed again was cleared again: it goes back to waiting, in the same entry.
            if (column.Sources.ContainsKey(pos))
            {
                column.Sources[pos] = false;
            }
            else if (PendingSourceCount < MaxPendingSources)
            {
                column.Sources[pos] = false;
                PendingSourceCount++;
            }
            else
            {
                WarnCapOnce();
            }
        }
    }

    /// <summary>
    /// One pass over a column: tests its gate once, queues the waiting sources when it is open,
    /// and drops the queued ones the engine has lit. A closed column costs one gate test.
    /// </summary>
    private void AdvanceColumn((int Dim, int Cx, int Cz) key, Column column, HashSet<int> busyDimensions)
    {
        if (!_engine.IsGateOpen(key.Cx, key.Cz))
        {
            // The engine may have dropped what was queued just before the column closed.
            if (column.AnyQueued)
            {
                column.MarkAllWaiting();
            }

            return;
        }

        bool queuedAny = false;
        foreach (var pos in new List<BlockPos>(column.Sources.Keys))
        {
            bool wasQueued = column.Sources[pos];
            if (IsSettled(pos, wasQueued))
            {
                column.Sources.Remove(pos);
                continue;
            }

            column.Sources[pos] = true;
            queuedAny |= !wasQueued;
            busyDimensions.Add(key.Dim);
        }

        column.AnyQueued = column.Sources.Count > 0;
        if (queuedAny && column.SendToClients)
        {
            NoteQueued(key);
        }
    }

    /// <summary>
    /// Whether a source needs no more attention. A waiting source is queued (even when it already
    /// reads as lit: a source just outside the cleared chunks kept its own light but lost what it
    /// shone into them) and is settled only if nothing emits light there. A queued source is
    /// settled once it is lit, or once the block is gone. A source that throws (third-party block
    /// code runs here) is logged once and treated as settled so it cannot be retried forever.
    /// </summary>
    private bool IsSettled(BlockPos pos, bool queued)
    {
        try
        {
            return queued
                ? _engine.IsLit(pos) || !_engine.EmitsLight(pos)
                : !_engine.QueueBlockLight(pos);
        }
        catch (Exception ex)
        {
            WarnFailureOnce($"the light source at {pos}", ex);
            return true;
        }
    }

    /// <summary>Light was queued in a column: its chunks, and those it shines into, need a resend when it lands.</summary>
    private void NoteQueued((int Dim, int Cx, int Cz) key)
    {
        Resend resend = ResendFor(key.Dim);
        resend.AwaitingLight = true;
        try
        {
            // The whole column, whatever the height of its sources: the engine clamps to the map.
            int x = key.Cx * ChunkMath.ChunkSize;
            int z = key.Cz * ChunkMath.ChunkSize;
            int last = ChunkMath.ChunkSize - 1;
            _engine.CollectAffectedChunks(
                new BlockPos(x, 0, z, key.Dim), new BlockPos(x + last, ColumnTop, z + last, key.Dim), resend.Chunks);
        }
        catch (Exception ex)
        {
            WarnFailureOnce("listing the chunks to resend", ex);
        }
    }

    /// <summary>
    /// Resends a dimension's chunks once none of its sources is being computed. The engine writes
    /// a source's own position first and the rest of its sphere right after, on another thread, so
    /// the resend waits one more pass after the last source reads as lit. Sources still waiting
    /// for a closed column do not hold the resend back: their chunks are resent again when they
    /// are finally lit.
    /// </summary>
    private void FlushResends(HashSet<int> busyDimensions)
    {
        foreach (int dimId in new List<int>(_resends.Keys))
        {
            Resend resend = _resends[dimId];
            if (busyDimensions.Contains(dimId))
            {
                continue;
            }

            if (resend.AwaitingLight)
            {
                resend.AwaitingLight = false;
                continue;
            }

            _resends.Remove(dimId);
            try
            {
                _engine.Resend(dimId, resend.Chunks);
            }
            catch (Exception ex)
            {
                WarnFailureOnce("resending the relit chunks", ex);
            }
        }
    }

    private Resend ResendFor(int dimId)
    {
        if (!_resends.TryGetValue(dimId, out var resend))
        {
            resend = new Resend();
            _resends[dimId] = resend;
        }

        return resend;
    }

    private void RecountPending()
    {
        int count = 0;
        foreach (var column in _columns.Values)
        {
            count += column.Sources.Count;
        }

        PendingSourceCount = count;
        if (count == 0)
        {
            _capWarned = false;
        }
    }

    private void WarnCapOnce()
    {
        if (_capWarned)
        {
            return;
        }

        _capWarned = true;
        _engine.Warn(
            $"[Manifold] Relight: more than {MaxPendingSources} light sources are waiting to be lit; the rest of this "
            + "relight is not restored and stays dark until the next relight. Relight smaller areas, or wait until a player is near.");
    }

    private void WarnFailureOnce(string what, Exception ex)
    {
        if (_failureWarned)
        {
            return;
        }

        _failureWarned = true;
        _engine.Warn($"[Manifold] Relight: {what} failed and was skipped (further failures are not logged): {ex}");
    }

    private sealed class Column
    {
        /// <summary>Gets the pending sources of the column: position to "already queued".</summary>
        public Dictionary<BlockPos, bool> Sources { get; } = new();

        public long DeadlineMs { get; set; }

        public bool SendToClients { get; set; }

        public bool AnyQueued { get; set; }

        public void MarkAllWaiting()
        {
            AnyQueued = false;
            foreach (var pos in new List<BlockPos>(Sources.Keys))
            {
                Sources[pos] = false;
            }
        }
    }

    private sealed class Resend
    {
        public HashSet<(int Cx, int Cy, int Cz)> Chunks { get; } = new();

        public bool AwaitingLight { get; set; }
    }
}
