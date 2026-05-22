using Manifold.Api;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Transitions;

/// <summary>
/// Resolves the target <see cref="BlockPos"/> in the destination dimension for a transit.
/// </summary>
/// <remarks>
/// May be called more than once per transit; implementations should be deterministic and side-effect free.
/// The transit service first calls <see cref="Resolve"/> to determine the generation region center, then
/// generates terrain, then calls <see cref="Resolve"/> again so the result can query the freshly generated
/// blocks (e.g. to find the surface Y after generation).
/// </remarks>
public interface ITargetPositionResolver
{
    /// <summary>Compute the target landing position for a transit.</summary>
    /// <param name="entity">The entity undergoing transit (a player's entity, or a non-player entity).</param>
    /// <param name="target">Destination dimension.</param>
    /// <param name="api">Server API for surface queries.</param>
    /// <returns>A dimension-encoded <see cref="BlockPos"/>.</returns>
    BlockPos Resolve(Entity entity, IDimension target, ICoreServerAPI api);
}
