using System.Collections.Generic;
using Manifold.Api.Worldgen;

namespace Manifold.Pure.Tests.Fakes;

/// <summary>A worldgen strategy that records all calls for assertion in tests.</summary>
internal sealed class RecordingWorldgenStrategy : IWorldgenStrategy
{
    /// <summary>Number of times <see cref="OnInitialize"/> has been called.</summary>
    public int InitCallCount { get; private set; }

    /// <summary>Dimension ids passed to <see cref="GenerateColumn"/>, in order.</summary>
    public List<int> GenerateColumnCalls { get; } = new();

    /// <summary>When set, the next <see cref="OnInitialize"/> call throws instead of succeeding, then clears itself.</summary>
    public bool ThrowOnNextInitialize { get; set; }

    /// <inheritdoc/>
    public void OnInitialize(IWorldgenInitContext ctx)
    {
        InitCallCount++;
        if (ThrowOnNextInitialize)
        {
            ThrowOnNextInitialize = false;
            throw new System.InvalidOperationException("boom");
        }
    }

    /// <inheritdoc/>
    public void GenerateColumn(IWorldgenChunkContext ctx) =>
        GenerateColumnCalls.Add(ctx.DimensionId);
}
