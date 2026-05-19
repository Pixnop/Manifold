namespace Manifold.Api.Worldgen;

/// <summary>
/// Implementation contract for a dimension's procedural generation.
/// Attach an instance to a dimension via <c>IDimensionBuilder.WithWorldgen</c>.
/// </summary>
/// <remarks>
/// Server-side. Manifold drives generation actively: it calls <see cref="OnInitialize"/> once
/// per dimension, then <see cref="GenerateColumn"/> for each chunk column in the generated region.
/// (The engine's pass pipeline does NOT run for custom dimensions, so there is no per-pass concept.)
/// </remarks>
public interface IWorldgenStrategy
{
    /// <summary>Called once per dimension before the first column is generated. Resolve block ids here.</summary>
    /// <param name="ctx">Initialisation context.</param>
    void OnInitialize(IWorldgenInitContext ctx);

    /// <summary>Fill one chunk column. Use <c>ctx.BlockAccessor.SetBlock</c> with dimension-encoded positions from <c>ctx</c>.</summary>
    /// <param name="ctx">Per-column context.</param>
    void GenerateColumn(IWorldgenChunkContext ctx);
}
