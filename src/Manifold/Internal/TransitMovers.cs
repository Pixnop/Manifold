using System;

namespace Manifold.Internal;

/// <summary>
/// The three transit movement primitives, grouped so <see cref="TransitService"/> stays at a
/// reasonable constructor arity.
/// </summary>
/// <param name="Player">Player teleporter abstraction (used by <c>TeleportPlayer</c>).</param>
/// <param name="Entity">Cross-dimension entity re-home abstraction (used by <c>TeleportEntity</c>).</param>
/// <param name="Block">Cross-dimension block + BlockEntity transit abstraction (used by <c>TeleportBlock</c>).</param>
internal sealed record TransitMovers(IPlayerTeleporter Player, IEntityMover Entity, IBlockMover Block)
{
    /// <summary>Validates that all movers are non-null and returns this instance.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentNullException">Any mover is null.</exception>
    public TransitMovers Required()
    {
        ArgumentNullException.ThrowIfNull(Player);
        ArgumentNullException.ThrowIfNull(Entity);
        ArgumentNullException.ThrowIfNull(Block);
        return this;
    }
}
