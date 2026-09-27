namespace Manifold.Api;

/// <summary>
/// Thrown by <c>ITransitionService.TeleportPlayer</c>/<c>TeleportEntity</c>/<c>TeleportBlock</c>,
/// <c>IManifoldServer.RelightRegion</c> and <c>ForceRemoveDimension</c> when Manifold's Harmony
/// patches failed to apply at boot. Dimension registry calls (<c>Define</c>, <c>RegisterStatic</c>,
/// <c>Create</c>, <c>TryRemove</c>) are not affected: they still succeed, against a registry that
/// has no in-game effect.
/// </summary>
public sealed class ManifoldUnhealthyException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="ManifoldUnhealthyException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public ManifoldUnhealthyException(string message)
        : base(message)
    {
    }
}
