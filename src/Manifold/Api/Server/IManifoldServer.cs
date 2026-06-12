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
}
