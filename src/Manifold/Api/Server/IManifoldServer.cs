using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Server;

/// <summary>
/// Server-side facade for Manifold's core services.
/// </summary>
/// <remarks>
/// Initialized once per server session and obtained via <see cref="Helpers.ManifoldAccess.GetServer"/>.
/// </remarks>
public interface IManifoldServer
{
    /// <summary>Gets the dimension registry.</summary>
    IDimensionRegistry Registry { get; }

    /// <summary>Gets the transition service.</summary>
    ITransitionService Transitions { get; }

    /// <summary>Always <c>true</c>. Kept for compatibility with mods compiled against earlier versions.</summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Recomputes light in the block region between <paramref name="min"/> and <paramref name="max"/>
    /// inside the given dimension. Use after placing blocks without relight (a schematic paste, a
    /// structure stamp, a <c>ColumnGenerated</c> decoration, light sources placed by a worldgen
    /// strategy): the engine's own relight paths and the vanilla <c>/debug chunk relight</c> command
    /// are dimension-blind. The dimension field of both positions is overwritten with the target
    /// dimension's id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synchronous part: sunlight is recomputed before the call returns, by the engine's
    /// <c>FullRelight</c>, over the box plus one chunk in every direction. That pass costs a few
    /// hundred milliseconds even for a small box, and in a dimension open to the sky it floods the
    /// relit columns with full skylight (see <c>WithDarkSky</c>), so keep regions bounded and
    /// prefer one call over an area to one call per column.
    /// </para>
    /// <para>
    /// Asynchronous part: block light. <c>FullRelight</c> erases block light and cannot put it back
    /// in a custom dimension, so Manifold then hands every light source of the affected chunks
    /// (those the engine already tracks, and every light-emitting block, including ones written
    /// without relight) to the engine's own lighting queue. The engine computes it on its relight
    /// thread, typically within milliseconds, and the affected chunks are resent to the clients in
    /// range once it is done, about half a second after the call. Block light therefore reads 0 for
    /// a moment right after the call returns. No block is changed and no block entity is touched.
    /// </para>
    /// <para>
    /// The engine only computes block light for a column whose overworld map chunk (same X/Z) is
    /// loaded, which is the case around any player, whatever dimension the player is in. Light
    /// sources in a column nobody is near stay pending in memory and are retried until a player
    /// comes near, for up to five minutes; after that they are dropped with one warning in the
    /// server log and stay dark until the next call. Pending sources are not saved: a server
    /// restart forgets them, and removing the dimension drops them. A relight requested for an area
    /// nobody is near (at boot, right after <c>GenerateRegion</c>) may therefore only take effect
    /// once a player is there; call it when a player has entered the dimension to be sure.
    /// </para>
    /// <para>Best-effort: a failure inside the engine's relight is logged, never thrown.</para>
    /// </remarks>
    /// <param name="dimension">Dimension code to relight in (e.g. <c>mymod:vault</c>).</param>
    /// <param name="min">Minimum corner of the region (local coordinates).</param>
    /// <param name="max">Maximum corner of the region (local coordinates).</param>
    /// <exception cref="System.ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="DimensionNotFoundException">No dimension with that code.</exception>
    void RelightRegion(AssetLocation dimension, BlockPos min, BlockPos max);

    /// <summary>
    /// Forcibly removes an Ephemeral dimension even if players are still inside it: every occupant is
    /// first evacuated to the overworld (last-visited position), then the dimension is removed
    /// (firing <c>IDimensionRegistry.Destroyed</c>). Use for deliberate teardown of an occupied
    /// instance (session ended, arena closed). The everyday case - a dimension emptying on its own -
    /// is handled automatically; the plain <see cref="IDimensionRegistry.TryRemove"/> refuses while
    /// occupied so it never strands a player.
    /// </summary>
    /// <param name="dimension">The dimension to remove.</param>
    /// <returns>
    /// <c>true</c> if the dimension was removed (including when evacuating the last occupant already
    /// auto-reaped it); <c>false</c> if no dimension with that code exists, or if an occupant could
    /// not be evacuated and the dimension is therefore still in use.
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="dimension"/> is null.</exception>
    /// <exception cref="DimensionBuiltInImmutableException">The dimension is the built-in overworld.</exception>
    /// <exception cref="DimensionStateException">The dimension is Persistent (use the admin purge).</exception>
    bool ForceRemoveDimension(AssetLocation dimension);

    /// <summary>
    /// Generates or loads the region around <paramref name="center"/>'s chunk column in
    /// <paramref name="dimension"/> - <c>GenerationRadius</c> chunks in each direction, exactly as a
    /// player transit does - without moving anyone. Use this to pregenerate a dimension's terrain
    /// before its first transit: registering a dimension (<c>RegisterStatic</c>/<c>Create</c>)
    /// reserves an engine id but touches no chunk, so a statically registered dimension has no
    /// terrain until something visits it.
    /// Synchronous; cost scales with <c>GenerationRadius</c>. Server main thread only.
    /// </summary>
    /// <param name="dimension">Dimension code to generate.</param>
    /// <param name="center">
    /// World position whose X/Z locate the chunk column to center generation on; its Y and
    /// <see cref="Vintagestory.API.MathTools.BlockPos.dimension"/> fields are ignored.
    /// </param>
    /// <exception cref="System.ArgumentNullException"><paramref name="dimension"/> or <paramref name="center"/> is null.</exception>
    /// <exception cref="DimensionNotFoundException">No dimension with that code.</exception>
    /// <exception cref="DimensionStateException">The dimension is not <see cref="DimensionState.Active"/>.</exception>
    void GenerateRegion(AssetLocation dimension, BlockPos center);

    /// <summary>
    /// The online players currently inside <paramref name="dimension"/>, determined from each
    /// player's live position dimension id - the same live check <see cref="IDimensionRegistry.TryRemove"/>
    /// uses to refuse removing an occupied dimension. Server main thread only.
    /// </summary>
    /// <param name="dimension">Dimension code to query.</param>
    /// <returns>The occupants, in no particular order; empty if none are inside.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="dimension"/> is null.</exception>
    /// <exception cref="DimensionNotFoundException">No dimension is registered with that code.</exception>
    IReadOnlyList<IServerPlayer> GetPlayersIn(AssetLocation dimension);
}
