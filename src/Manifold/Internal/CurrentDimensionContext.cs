using System;
using System.Threading;

namespace Manifold.Internal;

/// <summary>
/// Per-worker-thread carrier for "which dimension is currently being generated".
/// Pushed by the Harmony patch (Phase 9) around the engine's chunk-gen invocation
/// and read by the <see cref="WorldgenDispatcher"/> inside its handler.
/// </summary>
internal static class CurrentDimensionContext
{
    private static readonly AsyncLocal<int> _current = new();

    /// <summary>Current dimension id for the calling logical-flow / thread. Defaults to 0 (overworld).</summary>
    public static int Current => _current.Value;

    /// <summary>Push a new current value; dispose to restore the previous one.</summary>
    /// <param name="dimensionId">Dimension id to set as current.</param>
    /// <returns>A disposable scope that restores the previous value when disposed.</returns>
    public static IDisposable Push(int dimensionId)
    {
        int previous = _current.Value;
        _current.Value = dimensionId;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly int _previous;
        private bool _disposed;

        public Scope(int previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current.Value = _previous;
        }
    }
}
