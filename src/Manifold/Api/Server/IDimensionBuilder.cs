using Manifold.Api.Worldgen;

namespace Manifold.Api.Server;

/// <summary>
/// Fluent builder for declaring a dimension to Manifold.
/// </summary>
/// <remarks>Server-side. Single use: <see cref="RegisterStatic"/> or <see cref="Create"/> finalises the builder.</remarks>
public interface IDimensionBuilder
{
    /// <summary>Attach the procedural-generation strategy. Required.</summary>
    /// <param name="strategy">Worldgen strategy implementation.</param>
    /// <returns>This builder, for chaining.</returns>
    IDimensionBuilder WithWorldgen(IWorldgenStrategy strategy);

    /// <summary>Marks the dimension as persistent. Mutually exclusive with <see cref="Ephemeral"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    IDimensionBuilder Persistent();

    /// <summary>Marks the dimension as ephemeral. Mutually exclusive with <see cref="Persistent"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    IDimensionBuilder Ephemeral();

    /// <summary>
    /// Finalises as a static, persistent dimension (boot-time use). Idempotent across server restarts —
    /// re-calling with the same code reuses the existing internal id.
    /// </summary>
    /// <returns>The registered dimension.</returns>
    IDimension RegisterStatic();

    /// <summary>
    /// Finalises as a runtime-created dimension. Lifetime must be explicit (see <see cref="Persistent"/>/<see cref="Ephemeral"/>).
    /// </summary>
    /// <returns>The newly created dimension.</returns>
    IDimension Create();
}
