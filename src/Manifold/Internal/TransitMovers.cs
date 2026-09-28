namespace Manifold.Internal;

/// <summary>
/// The transit movement primitives, grouped so <see cref="TransitService"/> stays at a
/// reasonable constructor arity.
/// </summary>
/// <param name="Player">Player teleporter abstraction (used by <c>TeleportPlayer</c>).</param>
/// <param name="Entity">Cross-dimension entity re-home abstraction (used by <c>TeleportEntity</c>).</param>
/// <param name="Block">Cross-dimension block + BlockEntity transit abstraction (used by <c>TeleportBlock</c>).</param>
/// <param name="Dismounter">Releases a mounted player before a player transit.</param>
internal sealed record TransitMovers(IPlayerTeleporter Player, IEntityMover Entity, IBlockMover Block, IPlayerDismounter Dismounter);
