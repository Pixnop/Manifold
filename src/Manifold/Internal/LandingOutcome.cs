namespace Manifold.Internal;

/// <summary>What <see cref="LandingTracker.Complete"/> asks the teleporter to do once the engine has applied a teleport.</summary>
/// <param name="ApplyYaw">The yaw to set on the entity, or <c>null</c> to leave it alone.</param>
/// <param name="Reissue">A landing to teleport the player to again (the engine just moved them somewhere a newer teleport had already moved them away from), or <c>null</c>.</param>
internal readonly record struct LandingOutcome(float? ApplyYaw, PendingLanding? Reissue);
