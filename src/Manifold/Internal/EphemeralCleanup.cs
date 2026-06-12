using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Server;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Removes every <see cref="DimensionLifetime.Ephemeral"/> dimension from a registry. Wired
/// from server shutdown so companions subscribing to <c>IDimensionRegistry.Destroyed</c> (or its
/// client-side mirror <c>IManifoldClient.Destroyed</c>) get a chance to clean up state owned
/// by an ephemeral dim (e.g. cached map tiles) before the manifest is rewritten without it.
/// </summary>
internal static class EphemeralCleanup
{
    /// <summary>
    /// Calls <see cref="IDimensionRegistry.TryRemove"/> on every Ephemeral dimension currently in
    /// <paramref name="registry"/>. Snapshots the codes first since <c>TryRemove</c> mutates the
    /// registry. Returns the number of dimensions actually removed.
    /// </summary>
    /// <param name="registry">Dimension registry to scan.</param>
    /// <returns>Number of Ephemeral dimensions removed.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="registry"/> is null.</exception>
    public static int RemoveAll(IDimensionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var ephemeralCodes = new List<AssetLocation>();
        foreach (var dim in registry.All)
        {
            if (dim.Lifetime == DimensionLifetime.Ephemeral)
            {
                ephemeralCodes.Add(dim.Code);
            }
        }

        int removed = 0;
        foreach (var code in ephemeralCodes)
        {
            if (registry.TryRemove(code))
            {
                removed++;
            }
        }

        return removed;
    }
}
