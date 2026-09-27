namespace Manifold.Api.Worldgen;

/// <summary>
/// Implementation contract for a dimension's procedural generation.
/// Attach an instance to a dimension via <c>IDimensionBuilder.WithWorldgen</c>.
/// </summary>
/// <remarks>
/// Server-side. Manifold drives generation actively: it calls <see cref="OnInitialize"/>, then
/// <see cref="GenerateColumn"/> for each chunk column in the generated region. (The engine's pass
/// pipeline does NOT run for custom dimensions, so there is no per-pass concept.)
/// <para>
/// <see cref="OnInitialize"/> runs lazily, on the first generation request of each server session,
/// and is retried on the next request if it throws. Exceptions thrown by either method are caught
/// and logged, never propagated: they are not visible to callers of <c>TeleportPlayer</c> and
/// similar. After 4 consecutive failures, generation for the dimension is disabled until the next
/// restart and transits land in ungenerated space. A column whose <see cref="GenerateColumn"/>
/// threw is still committed and marked generated, and is not retried.
/// </para>
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
