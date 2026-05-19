using Manifold.Api.Worldgen;

namespace Manifold.Api.Helpers;

/// <summary>A worldgen strategy that leaves the dimension empty (all air).</summary>
/// <remarks>Chunks are allocated as air; this strategy intentionally does nothing.</remarks>
public sealed class BasicVoidWorldgenStrategy : IWorldgenStrategy
{
    /// <inheritdoc/>
    public void OnInitialize(IWorldgenInitContext ctx)
    {
    }

    /// <inheritdoc/>
    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
    }
}
