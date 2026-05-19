using Vintagestory.API.MathTools;

namespace Manifold.Api.Transitions;

/// <summary>
/// Optional knobs for <see cref="Server.ITransitionService.TeleportPlayer"/>.
/// </summary>
public readonly record struct TransitionOptions
{
    /// <summary>Initializes a new instance of the <see cref="TransitionOptions"/> struct.</summary>
    public TransitionOptions()
    {
        PreserveInventory = true;
    }

    /// <summary>If set, overrides the resolver-computed target position. Caller-supplied dim encoding mandatory.</summary>
    public BlockPos? OverridePosition { get; init; }

    /// <summary>Resolver used when <see cref="OverridePosition"/> is null. Defaults to <see cref="TargetPositionResolvers.SameXZSurfaceY"/>.</summary>
    public ITargetPositionResolver? Resolver { get; init; }

    /// <summary>If <c>true</c> (default), the player's inventory remains untouched across transit.</summary>
    public bool PreserveInventory { get; init; }
}
