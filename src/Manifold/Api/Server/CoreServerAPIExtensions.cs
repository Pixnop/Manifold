using System;
using Manifold.Api;
using Manifold.Api.Helpers;
using Vintagestory.API.Server;

namespace Manifold.Api.Server;

/// <summary>Extension method to obtain Manifold's server facade.</summary>
public static class CoreServerAPIExtensions
{
    /// <summary>Get Manifold's server-side facade.</summary>
    /// <param name="sapi">Server API.</param>
    /// <returns>The facade.</returns>
    /// <exception cref="ManifoldNotInitializedException">
    /// Manifold's <c>ModSystem</c> is not loaded or its <c>StartServerSide</c> has not yet run.
    /// </exception>
    public static IManifoldServer GetManifoldServer(this ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        return ManifoldAccess.GetServer(sapi)
            ?? throw new ManifoldNotInitializedException(
                "Manifold facade not available. Either the mod is not loaded "
                + "or Manifold's StartServerSide has not run yet. Ensure your mod "
                + "declares 'manifold' as a dependency in modinfo.json.");
    }
}
