using System.Collections.Generic;
using Manifold.Api.Worldgen;
using Vintagestory.API.Server;

namespace Manifold.Pure.Tests.Fakes;

internal sealed class RecordingWorldgenStrategy : IWorldgenStrategy
{
    public IReadOnlySet<EnumWorldGenPass> Passes { get; init; } =
        new HashSet<EnumWorldGenPass> { EnumWorldGenPass.PreDone };

    public int InitCallCount { get; private set; }

    public List<(int Dim, EnumWorldGenPass Pass)> ChunkGenCalls { get; } = new();

    public void OnInitialize(IWorldgenInitContext ctx) => InitCallCount++;

    public void OnChunkColumnGen(IWorldgenChunkContext ctx, EnumWorldGenPass pass) =>
        ChunkGenCalls.Add((ctx.DimensionId, pass));
}
