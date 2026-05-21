using Manifold.Api.Transitions;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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
    /// Sets the generation radius in chunks around the transit target (default 2 = 5x5 columns).
    /// Larger values generate more terrain per transit but cost more time. Range 0..16.
    /// </summary>
    /// <param name="chunks">Radius in chunks (0 = only the target column).</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithGenerationRadius(int chunks);

    /// <summary>
    /// Sets the upper Y bound for the post-generation relight pass (default 20). Content built
    /// above this height is under-lit until the engine relights naturally. Higher values light
    /// taller dimensions correctly but cost more per relight. Range 1..1024.
    /// </summary>
    /// <param name="maxY">Top of the lit band.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithRelightHeight(int maxY);

    /// <summary>Sets how players land when entering this dimension. Default: <see cref="SpawnBehavior.SameCoordinates"/>.</summary>
    /// <param name="behavior">The spawn behavior.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithSpawnBehavior(SpawnBehavior behavior);

    /// <summary>Sets a fixed spawn point and switches spawn behavior to <see cref="SpawnBehavior.DimensionSpawn"/>.</summary>
    /// <param name="spawnPoint">The fixed landing position.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithFixedSpawn(BlockPos spawnPoint);

    /// <summary>Forces a game mode when players enter this dimension. Omit to preserve the player's current mode.</summary>
    /// <param name="mode">The game mode to force on entry.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithForcedGameMode(EnumGameMode mode);

    /// <summary>
    /// Opts the dimension into streaming worldgen: chunks are generated on demand as players move,
    /// keeping a radius of <paramref name="loadRadius"/> chunks around each player. Range 1..32.
    /// Omit for bounded generation (see <see cref="WithGenerationRadius"/>).
    /// </summary>
    /// <param name="loadRadius">Chunk radius kept generated around each player.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder Streaming(int loadRadius);

    /// <summary>
    /// Finalises as a static, persistent dimension (boot-time use). Idempotent across server restarts -
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
