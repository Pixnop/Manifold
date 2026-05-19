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

    /// <inheritdoc/>
    public void OnInitialize(IWorldgenInitContext ctx) => InitCallCount++;

    /// <inheritdoc/>
    public void GenerateColumn(IWorldgenChunkContext ctx) =>
        GenerateColumnCalls.Add(ctx.DimensionId);
}
