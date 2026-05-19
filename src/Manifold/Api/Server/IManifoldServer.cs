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
}
