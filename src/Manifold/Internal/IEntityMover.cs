using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>Re-homes a non-player entity into a target dimension at a dimension-encoded position.</summary>
/// <remarks>Server-side, main thread. Abstracted so <see cref="TransitService"/> stays unit-testable.</remarks>
internal interface IEntityMover
{
    /// <summary>Moves the entity to the given position, whose <see cref="BlockPos.dimension"/> is the target dimension.</summary>
    /// <param name="entity">The entity to move.</param>
    /// <param name="targetWithDimension">Dimension-encoded local target position.</param>
    void Move(Entity entity, BlockPos targetWithDimension);
}
