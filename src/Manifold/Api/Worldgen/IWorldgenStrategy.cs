using System.Collections.Generic;
using Vintagestory.API.Server;

namespace Manifold.Api.Worldgen;

/// <summary>
/// Implementation contract for a dimension's procedural generation.
/// Attach an instance to a dimension via <c>IDimensionBuilder.WithWorldgen</c>.
/// </summary>
/// <remarks>Server-side. Each method runs on a worldgen worker thread.</remarks>
public interface IWorldgenStrategy
{
    /// <summary>
    /// Set of passes this strategy listens to.
    /// Must be non-null and non-empty; values from <see cref="EnumWorldGenPass"/>.
    /// Inspected once at <c>WithWorldgen</c> time; later mutations are ignored.
    /// </summary>
    IReadOnlySet<EnumWorldGenPass> Passes { get; }

    /// <summary>Called once per worker thread at first use. Use to resolve block ids, seed RNGs, etc.</summary>
    /// <param name="ctx">Init context.</param>
    void OnInitialize(IWorldgenInitContext ctx);

    /// <summary>Called for each chunk column in each pass declared in <see cref="Passes"/>.</summary>
    /// <param name="ctx">Chunk context.</param>
    /// <param name="pass">Current worldgen pass.</param>
    void OnChunkColumnGen(IWorldgenChunkContext ctx, EnumWorldGenPass pass);
}
