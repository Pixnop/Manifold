using System.Collections.Generic;
using Manifold.Api.Worldgen;
using Vintagestory.API.Server;

namespace Manifold.Pure.Tests.Fakes;

internal sealed class FakeWorldgenStrategy : IWorldgenStrategy
{
    public IReadOnlySet<EnumWorldGenPass> Passes { get; init; } =
        new HashSet<EnumWorldGenPass> { EnumWorldGenPass.PreDone };

    public void OnInitialize(IWorldgenInitContext ctx)
    {
    }

    public void OnChunkColumnGen(IWorldgenChunkContext ctx, EnumWorldGenPass pass)
    {
    }
}
