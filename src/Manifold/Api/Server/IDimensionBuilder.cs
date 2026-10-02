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

    /// <summary>
    /// Marks the dimension as persistent. Mutually exclusive with <see cref="Ephemeral"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Lifetime was already set to <see cref="Ephemeral"/>, or the builder was already finalised.
    /// </exception>
    /// <remarks>
    /// A <see cref="DimensionState.Pending"/> entry is always seeded as <see cref="DimensionLifetime.Persistent"/>
    /// (only Persistent dimensions are written to the manifest), so completing one after this call
    /// never conflicts with its kept lifetime.
    /// </remarks>
    IDimensionBuilder Persistent();

    /// <summary>
    /// Marks the dimension as ephemeral. Mutually exclusive with <see cref="Persistent"/>. An
    /// ephemeral dimension is removed automatically when its last occupant transits out (firing
    /// <c>IDimensionRegistry.Destroyed</c>) and at server shutdown; its chunks are never persisted.
    /// Disconnecting does not remove it - a logged-out player reconnects back into it while the server
    /// is up. Use <see cref="Persistent"/> if the dimension must survive a restart.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Lifetime was already set to <see cref="Persistent"/>, or the builder was already finalised.
    /// </exception>
    /// <remarks>
    /// If <see cref="Create"/> then promotes an existing
    /// <see cref="DimensionState.Pending"/> entry (always <see cref="DimensionLifetime.Persistent"/>,
    /// seeded from the manifest), the Persistent lifetime is kept and this call has no effect on it;
    /// Manifold logs a warning naming the code, since the mismatch is usually a bug.
    /// </remarks>
    IDimensionBuilder Ephemeral();

    /// <summary>
    /// Sets the generation radius in chunks around the transit target (default 2 = 5x5 columns).
    /// Larger values generate more terrain per transit but cost more time. Range 0..16.
    /// </summary>
    /// <param name="chunks">Radius in chunks (0 = only the target column).</param>
    /// <returns>This builder.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="chunks"/> is outside 0..16.</exception>
    IDimensionBuilder WithGenerationRadius(int chunks);

    /// <summary>
    /// No effect since Manifold 0.4.2, which removed the automatic post-generation relight. Still
    /// validates that <paramref name="maxY"/> is in range 1..1024 and throws
    /// <see cref="System.ArgumentOutOfRangeException"/> otherwise, for binary compatibility with
    /// existing calls.
    /// </summary>
    /// <param name="maxY">Ignored except for range validation (range 1..1024).</param>
    /// <returns>This builder.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Info Code Smell", "S1133:Deprecated code should be removed", Justification = "Kept for binary compatibility with mods built against 0.4.x; remove in the next minor release.")]
    [System.Obsolete("No effect since Manifold 0.4.2, which removed the automatic post-generation relight. Use IManifoldServer.RelightRegion to relight after placing blocks.")]
    IDimensionBuilder WithRelightHeight(int maxY);

    /// <summary>Sets how players land when entering this dimension. Default: <see cref="SpawnBehavior.SameCoordinates"/>.</summary>
    /// <param name="behavior">The spawn behavior.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithSpawnBehavior(SpawnBehavior behavior);

    /// <summary>Sets a fixed spawn point and switches spawn behavior to <see cref="SpawnBehavior.DimensionSpawn"/>.</summary>
    /// <param name="spawnPoint">The fixed landing position.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithFixedSpawn(BlockPos spawnPoint);

    /// <summary>
    /// Forces a game mode when players enter this dimension. Omit to preserve the player's current
    /// mode. The player's mode from before the first forced dimension is saved (persisted in their
    /// moddata, so it survives logout and restarts) and restored when they next enter a dimension
    /// that forces no mode; chaining forced dimensions keeps that original mode rather than the
    /// previous forced one.
    /// </summary>
    /// <param name="mode">The game mode to force on entry.</param>
    /// <returns>This builder.</returns>
    IDimensionBuilder WithForcedGameMode(EnumGameMode mode);

    /// <summary>
    /// Opts the dimension into streaming worldgen: chunks are generated on demand as players move,
    /// keeping at least <paramref name="loadRadius"/> chunks generated around each player. Range
    /// 1..32. The effective radius is <c>max(loadRadius, server view distance)</c>, so a value below
    /// the server's view distance has no effect and behaves like the view distance instead.
    /// Omit for bounded generation (see <see cref="WithGenerationRadius"/>).
    /// </summary>
    /// <param name="loadRadius">Chunk radius kept generated around each player.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="loadRadius"/> is outside 1..32.</exception>
    IDimensionBuilder Streaming(int loadRadius);

    /// <summary>
    /// Caps how many columns the streaming driver may ensure for this dimension per streaming-driver
    /// tick (every 250 ms, not every server tick). Default is 4. Increase for dimensions with heavy
    /// traffic (a hub, a popular arena); decrease for background dimensions that should not compete
    /// with the main world for scheduler slots. Per-dimension caps are independent: a busy dim cannot
    /// starve a quiet one. Range 1..64. Only meaningful on streaming dimensions (combine with
    /// <see cref="Streaming"/>).
    /// </summary>
    /// <param name="maxColumnsPerTick">Per-dimension column budget per streaming-driver tick (range 1..64).</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="maxColumnsPerTick"/> is outside 1..64.</exception>
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
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="ceilingY"/> is outside 1..1024.</exception>
    /// <remarks>
    /// Best for enclosed / underground dimensions. The outermost ring of the generated region can
    /// still leak some light from the un-generated chunks beyond it; generate a chunk of margin
    /// around the playable area if that edge is visible. Solid-filled dimensions (terrain that is
    /// solid except for carved-out rooms) are dark without this option.
    /// </remarks>
    IDimensionBuilder WithDarkSky(int ceilingY);

    /// <summary>
    /// Opts the dimension into separate per-player inventories for the given categories. On entering
    /// the dimension the player's chosen inventories are swapped to this dimension's set and restored
    /// on leaving. Omit for a shared inventory.
    /// </summary>
    /// <param name="categories">Inventory categories to keep separate.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Per-dimension sets are stored per player, keyed by this dimension's code, in the player's
    /// moddata; they persist across restarts and are NOT cleared when the dimension is destroyed. A
    /// dimension later recreated with the same code restores each returning player's previous set
    /// rather than starting empty. Use a unique code per instance if each instance must start empty.
    /// </remarks>
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
    /// Metadata is replicated to client mirrors (since 0.6.0) and immutable once the dimension is
    /// registered, so a client can cache what it reads. It is not persisted across server restarts.
    /// For static dimensions this is harmless (the owner re-declares them on boot). For runtime
    /// <c>Create</c> dimensions, treat metadata as ephemeral.
    /// </remarks>
    IDimensionBuilder WithMetadata(string key, object? value);

    /// <summary>
    /// Finalises as a static, persistent dimension (boot-time use). Idempotent across server restarts -
    /// re-calling with the same code reuses the existing internal id.
    /// </summary>
    /// <returns>The registered dimension.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// The builder was already finalised, or <see cref="Ephemeral"/> was set (use <see cref="Create"/> instead).
    /// </exception>
    /// <exception cref="WorldgenStrategyContractException"><see cref="WithWorldgen"/> was not called.</exception>
    /// <exception cref="DimensionCapacityExceededException">No engine dimension id is available.</exception>
    IDimension RegisterStatic();

    /// <summary>
    /// Finalises as a runtime-created dimension. Lifetime must be explicit (see <see cref="Persistent"/>/<see cref="Ephemeral"/>).
    /// </summary>
    /// <returns>The newly created dimension.</returns>
    /// <exception cref="System.InvalidOperationException">The builder was already finalised.</exception>
    /// <exception cref="WorldgenStrategyContractException"><see cref="WithWorldgen"/> was not called.</exception>
    /// <exception cref="DimensionLifetimeUnspecifiedException">
    /// Neither <see cref="Persistent"/> nor <see cref="Ephemeral"/> was called.
    /// </exception>
    /// <exception cref="DimensionCapacityExceededException">No engine dimension id is available.</exception>
    IDimension Create();
}
