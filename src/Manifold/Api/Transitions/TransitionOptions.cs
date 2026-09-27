using Vintagestory.API.MathTools;

namespace Manifold.Api.Transitions;

/// <summary>
/// Optional knobs for <see cref="Server.ITransitionService.TeleportPlayer"/> and
/// <see cref="Server.ITransitionService.TeleportEntity"/> (<see cref="SpawnBehavior"/> applies to
/// players only).
/// </summary>
public readonly record struct TransitionOptions
{
    /// <summary>
    /// If set, used as the landing position instead of the resolver. Its dimension field is ignored
    /// and replaced with the target dimension's id on a copy; the instance you pass in is never mutated.
    /// </summary>
    public BlockPos? OverridePosition { get; init; }

    /// <summary>
    /// Custom resolver used when <see cref="OverridePosition"/> is null. Setting it replaces the
    /// destination dimension's configured <see cref="SpawnBehavior"/> entirely, for both players and
    /// entities. When null: players land per <see cref="SpawnBehavior"/> (this struct's, else the
    /// destination dimension's), falling back to <see cref="TargetPositionResolvers.SameXZSurfaceY"/>
    /// where that behavior needs one; non-player entities always use
    /// <see cref="TargetPositionResolvers.SameXZSurfaceY"/>.
    /// </summary>
    public ITargetPositionResolver? Resolver { get; init; }

    /// <summary>
    /// Per-transit spawn behavior override. When set, takes precedence over the destination
    /// dimension's configured behavior (but not over <see cref="OverridePosition"/> or <see cref="Resolver"/>).
    /// Useful for transiting to the built-in overworld with <see cref="SpawnBehavior.LastVisited"/>.
    /// </summary>
    public SpawnBehavior? SpawnBehavior { get; init; }
}
