using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Dimension-aware relight that keeps block light. The engine's <c>FullRelight</c> recomputes
/// sunlight correctly in a custom dimension but clears block light and puts it back at doubled
/// coordinates, so every light source outside chunk (0, 0, 0) ends up dark. After it runs, this
/// class hands each light source of the affected chunks back to the engine's own lighting queue
/// (the path a player placing a torch takes), which is asynchronous and silently drops work for a
/// column whose overworld map chunk is not loaded. Sources in such a column stay pending here and
/// are retried from <see cref="Tick"/> until the engine accepts them or <see cref="TimeoutMs"/>
/// passes. Nothing is persisted: a server restart forgets the pending sources.
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

    private readonly IRelightEngine _engine;
    private readonly Func<long> _clockMs;
    private readonly List<Request> _requests = new();

    /// <summary>Initializes a new instance of the <see cref="BlockLightRestorer"/> class.</summary>
    /// <param name="engine">Engine operations.</param>
    /// <param name="clockMs">Monotonic clock in milliseconds.</param>
    public BlockLightRestorer(IRelightEngine engine, Func<long> clockMs)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _clockMs = clockMs ?? throw new ArgumentNullException(nameof(clockMs));
    }

    /// <summary>Gets the number of light sources still waiting for the engine to light them.</summary>
    public int PendingSourceCount
    {
        get
        {
            int count = 0;
            foreach (var request in _requests)
            {
                count += request.Sources.Count;
            }

            return count;
        }
    }

    /// <summary>
    /// Relights the box in <paramref name="dimId"/>: sunlight synchronously, then block light
    /// through the engine's queue. The dimension of both corners is overwritten with
    /// <paramref name="dimId"/>, so a position built without one cannot relight the overworld.
    /// </summary>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="min">Minimum corner (local coordinates).</param>
    /// <param name="max">Maximum corner (local coordinates).</param>
    /// <param name="sendToClients">
    /// Whether to resend the affected chunks to clients in range once their block light is back
    /// (at once when the box holds no light source).
    /// </param>
    /// <returns><c>false</c> if the engine's relight threw (logged as a warning).</returns>
    public bool Relight(int dimId, BlockPos min, BlockPos max, bool sendToClients)
    {
        var minPos = new BlockPos(min.X, min.Y, min.Z, dimId);
        var maxPos = new BlockPos(max.X, max.Y, max.Z, dimId);
        if (!_engine.FullRelight(minPos, maxPos))
        {
            return false;
        }

        var sources = new List<Source>();
        foreach (var pos in _engine.FindLightSources(minPos, maxPos))
        {
            sources.Add(new Source(pos));
        }

        var request = new Request(minPos, maxPos, sendToClients, _clockMs() + TimeoutMs, sources);
        if (!Advance(request))
        {
            _requests.Add(request);
        }

        return true;
    }

    /// <summary>Retries the pending light sources. Call regularly from a server tick listener.</summary>
    public void Tick() => _requests.RemoveAll(Advance);

    /// <summary>Drops everything pending for a dimension that is being removed.</summary>
    /// <param name="dimId">Engine dimension id.</param>
    public void ForgetDimension(int dimId) => _requests.RemoveAll(r => r.Min.dimension == dimId);

    /// <summary>Moves a request forward. Returns <c>true</c> once it is finished.</summary>
    private bool Advance(Request request)
    {
        request.Sources.RemoveAll(IsSettled);
        if (request.Sources.Count == 0)
        {
            // The engine writes a source's own position first and the rest of its sphere right
            // after, on another thread: wait one more pass before resending so clients do not get
            // a half-lit chunk. Nothing to wait for when the box never had a light source.
            if (request.Settled)
            {
                return Finish(request);
            }

            request.Settled = true;
            return false;
        }

        if (_clockMs() < request.DeadlineMs)
        {
            return false;
        }

        _engine.Warn(
            $"[Manifold] Relight of dim {request.Min.dimension} {request.Min}..{request.Max}: {request.Sources.Count} "
            + $"light source(s) were not lit within {TimeoutMs / 1000} s because no player came near them "
            + "(the engine only lights a column whose overworld map chunk is loaded). They stay dark until the next relight.");
        return Finish(request);
    }

    private bool Finish(Request request)
    {
        if (request.SendToClients)
        {
            _engine.Broadcast(request.Min, request.Max);
        }

        return true;
    }

    /// <summary>
    /// Whether a source needs no more attention: queued and lit, or no longer a light source. Queues
    /// it the first time its column's gate is found open, even when it already reads as lit: a
    /// source just outside the cleared chunks kept its own light but lost what it shone into them.
    /// </summary>
    private bool IsSettled(Source source)
    {
        if (!source.Queued)
        {
            if (!_engine.IsGateOpen(source.Pos))
            {
                return false;
            }

            source.Queued = true;
            if (!_engine.QueueBlockLight(source.Pos))
            {
                return true;
            }
        }

        return _engine.IsLit(source.Pos);
    }

    private sealed class Source
    {
        public Source(BlockPos pos) => Pos = pos;

        public BlockPos Pos { get; }

        public bool Queued { get; set; }
    }

    private sealed class Request
    {
        public Request(BlockPos min, BlockPos max, bool sendToClients, long deadlineMs, List<Source> sources)
        {
            Min = min;
            Max = max;
            SendToClients = sendToClients;
            DeadlineMs = deadlineMs;
            Sources = sources;
            Settled = sources.Count == 0;
        }

        public BlockPos Min { get; }

        public BlockPos Max { get; }

        public bool SendToClients { get; }

        public long DeadlineMs { get; }

        public List<Source> Sources { get; }

        public bool Settled { get; set; }
    }
}
