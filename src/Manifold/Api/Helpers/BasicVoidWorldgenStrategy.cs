using System.Collections.Generic;
using Manifold.Api.Worldgen;
using Vintagestory.API.Server;

namespace Manifold.Api.Helpers;

/// <summary>
/// A worldgen strategy that produces an entirely empty (air) world.
/// </summary>
/// <remarks>
/// Useful for smoke testing and for dimensions whose contents are placed entirely by the consumer.
/// VS auto-allocates chunks as air; this strategy declares Terrain pass purely to satisfy
/// the registry validation that a strategy must declare at least one pass.
/// </remarks>
public sealed class BasicVoidWorldgenStrategy : IWorldgenStrategy
{
    /// <inheritdoc/>
    public IReadOnlySet<EnumWorldGenPass> Passes { get; } =
        new HashSet<EnumWorldGenPass> { EnumWorldGenPass.Terrain };

    /// <inheritdoc/>
    public void OnInitialize(IWorldgenInitContext ctx)
    {
        // Nothing — void worlds need no init.
    }

    /// <inheritdoc/>
    public void OnChunkColumnGen(IWorldgenChunkContext ctx, EnumWorldGenPass pass)
    {
        // Nothing — chunks default to air at allocation.
    }
}
