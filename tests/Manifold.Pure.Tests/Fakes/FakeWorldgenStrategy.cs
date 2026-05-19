using Manifold.Api.Worldgen;

namespace Manifold.Pure.Tests.Fakes;

/// <summary>A no-op worldgen strategy for use in tests.</summary>
internal sealed class FakeWorldgenStrategy : IWorldgenStrategy
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
