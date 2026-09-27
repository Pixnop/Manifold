using Vintagestory.API.MathTools;

namespace Manifold.Api.Transitions;

/// <summary>
/// Optional knobs for <see cref="Server.ITransitionService.TeleportPlayer"/>.
/// </summary>
public readonly record struct TransitionOptions
{
    /// <summary>
    /// If set, used as the landing position instead of the resolver. Its dimension field is ignored
    /// and replaced with the target dimension's id on a copy; the instance you pass in is never mutated.
    /// </summary>
    public BlockPos? OverridePosition { get; init; }

    /// <summary>Resolver used when <see cref="OverridePosition"/> is null. Defaults to <see cref="TargetPositionResolvers.SameXZSurfaceY"/>.</summary>
    public ITargetPositionResolver? Resolver { get; init; }

    /// <summary>
    /// Per-transit spawn behavior override. When set, takes precedence over the destination
    /// dimension's configured behavior (but not over <see cref="OverridePosition"/> or <see cref="Resolver"/>).
    /// Useful for transiting to the built-in overworld with <see cref="SpawnBehavior.LastVisited"/>.
    /// </summary>
    public SpawnBehavior? SpawnBehavior { get; init; }
}
