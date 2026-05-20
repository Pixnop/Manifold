using System;
using Manifold.Api;
using Manifold.Api.Helpers;
using Vintagestory.API.Client;

namespace Manifold.Api.Client;

/// <summary>Extension method to obtain Manifold's client facade.</summary>
public static class CoreClientApiExtensions
{
    /// <summary>Get Manifold's client-side facade.</summary>
    /// <param name="capi">Client API.</param>
    /// <returns>The facade.</returns>
    /// <exception cref="ManifoldNotInitializedException">
    /// Manifold's <c>ModSystem</c> is not loaded or its <c>StartClientSide</c> has not yet run.
    /// </exception>
    public static IManifoldClient GetManifoldClient(this ICoreClientAPI capi)
    {
        ArgumentNullException.ThrowIfNull(capi);
        return ManifoldAccess.GetClient(capi)
            ?? throw new ManifoldNotInitializedException(
                "Manifold client facade not available. Either the mod is not loaded "
                + "or Manifold's StartClientSide has not run yet. Ensure your mod "
                + "declares 'manifold' as a dependency in modinfo.json.");
    }
}
