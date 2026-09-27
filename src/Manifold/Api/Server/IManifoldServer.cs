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

    /// <summary>Returns <c>true</c> if Manifold successfully initialized all Harmony patches at boot.</summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Relights the block region between <paramref name="min"/> and <paramref name="max"/> inside
    /// the given dimension. Use after placing blocks at runtime (schematic paste, structure stamp)
    /// so light sources and sunlight are recalculated where the blocks actually are - the engine's
    /// own relight paths and the vanilla <c>/debug chunk relight</c> command are dimension-blind.
    /// The dimension field of both positions is overwritten with the target dimension's id.
    /// Best-effort and synchronous; cost scales with the relit volume, keep regions bounded.
    /// </summary>
    /// <param name="dimension">Dimension code to relight in (e.g. <c>mymod:vault</c>).</param>
    /// <param name="min">Minimum corner of the region (local coordinates).</param>
    /// <param name="max">Maximum corner of the region (local coordinates).</param>
    /// <exception cref="System.ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="DimensionNotFoundException">No dimension with that code.</exception>
    /// <exception cref="ManifoldUnhealthyException">Manifold failed to initialize at boot.</exception>
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
    /// <exception cref="ManifoldUnhealthyException">Manifold failed to initialize at boot.</exception>
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
    /// <exception cref="ManifoldUnhealthyException">Manifold failed to initialize at boot.</exception>
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
