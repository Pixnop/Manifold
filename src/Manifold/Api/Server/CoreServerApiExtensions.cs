using System;
using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Manifold.Api.Server;

/// <summary>Extension method to obtain Manifold's server facade.</summary>
public static class CoreServerApiExtensions
{
    /// <summary>Get Manifold's server-side facade (read/transit only - <c>Registry.Define</c> will throw).</summary>
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

    /// <summary>
    /// Get a Manifold server facade scoped to the calling mod, so dimensions you register are
    /// attributed to your mod id (required for correct quarantine on uninstall).
    /// </summary>
    /// <param name="sapi">Server API.</param>
    /// <param name="caller">Your mod system (its <c>Mod.Info.ModID</c> is recorded as the dimension owner).</param>
    /// <returns>An owner-scoped facade whose <c>Registry.Define</c> records the correct owner.</returns>
    /// <exception cref="ManifoldNotInitializedException">Manifold not loaded or not started.</exception>
    public static IManifoldServer GetManifoldServer(this ICoreServerAPI sapi, ModSystem caller)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        ArgumentNullException.ThrowIfNull(caller);
        var shared = sapi.GetManifoldServer();
        var ownerModId = caller.Mod?.Info?.ModID
            ?? throw new ManifoldNotInitializedException("Caller ModSystem has no Mod info.");
        if (shared.Registry is not DimensionRegistry sharedRegistry)
        {
            // Unhealthy or unexpected facade - return shared as-is (Define will throw clearly).
            return shared;
        }

        return new OwnerScopedManifoldServer(shared, sharedRegistry, ownerModId);
    }
}
