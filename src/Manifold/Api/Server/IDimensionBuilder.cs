using Manifold.Api;
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
    /// Caps how many columns the streaming driver may ensure for this dimension per tick. Default
    /// is 4. Increase for dimensions with heavy traffic (a hub, a popular arena); decrease for
    /// background dimensions that should not compete with the main world for scheduler slots.
    /// Per-dimension caps are independent: a busy dim cannot starve a quiet one. Range 1..64.
    /// Only meaningful on streaming dimensions (combine with <see cref="Streaming"/>).
    /// </summary>
    /// <param name="maxColumnsPerTick">Per-dimension column budget per tick (range 1..64).</param>
    /// <returns>This builder, for chaining.</returns>
    IDimensionBuilder WithStreamingBudget(int maxColumnsPerTick);

    /// <summary>
    /// Makes the dimension dark by sealing every generated column with an opaque ceiling at
    /// <paramref name="ceilingY"/>. Vintage Story floods skylight downward from the top of a
    /// dimension's column and does not gate it per dimension, so an open / mostly-air custom
    /// dimension renders fully lit regardless of the time of day. An opaque cap stops that flood:
    /// everything below <paramref name="ceilingY"/> stays dark and is lit only by block light
    /// (torches, lava, lamps). Capping every generated column also makes those chunks non-empty,
    /// which suppresses a client-side full-bright bleed from neighbouring empty chunks.
    /// </summary>
    /// <param name="ceilingY">Y of the opaque ceiling layer (range 1..1024). Place it one block above your tallest content.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Manifold raises the relight band to <c>ceilingY + 1</c> automatically (the cap must be inside
    /// the relit band to take effect), so you do not need to also call <see cref="WithRelightHeight"/>.
    /// Best for enclosed / underground dimensions. The outermost ring of the generated region can
    /// still leak some light from the un-generated chunks beyond it; generate a chunk of margin
    /// around the playable area if that edge is visible. Solid-filled dimensions (terrain that is
    /// solid except for carved-out rooms) are dark without this option.
    /// </remarks>
    IDimensionBuilder WithDarkSky(int ceilingY);

    /// <summary>
    /// Opts the dimension into separate per-player inventories for the given categories. On entering
    /// the dimension the player's chosen inventories are swapped to this dimension's set (empty on the
    /// first visit) and restored on leaving. Omit for a shared inventory.
    /// </summary>
    /// <param name="categories">Inventory categories to keep separate.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithSeparateInventory(ManifoldInventory categories);

    /// <summary>
    /// Attaches a typed metadata entry to the dimension, exposed through <see cref="IDimension.Metadata"/>.
    /// Useful for storing labels, categories, opt-in flags, and other registration-time hints that
    /// other systems can read without going through the owning mod.
    /// </summary>
    /// <param name="key">Metadata key. Must be non-empty.</param>
    /// <param name="value">Value. Supported: primitives, <c>string</c>, <c>enum</c>, <c>byte[]</c>, or <c>null</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="System.ArgumentException">Thrown when the value is of an unsupported type, or the same key is set twice.</exception>
    /// <remarks>
    /// Metadata is server-side only in v1; it is not replicated to client mirrors and not persisted
    /// across server restarts. For static dimensions this is harmless (the owner re-declares them on
    /// boot). For runtime <c>Create</c> dimensions, treat metadata as ephemeral.
    /// </remarks>
    IDimensionBuilder WithMetadata(string key, object? value);

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
