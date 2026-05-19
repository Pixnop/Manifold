using System;
using Manifold.Api.Server;
using Vintagestory.API.Server;

namespace Manifold.Api.Helpers;

/// <summary>
/// Static accessor providing helpers with an <see cref="IManifoldServer"/> facade.
/// Wired by <c>ManifoldModSystem</c> at boot.
/// </summary>
/// <remarks>
/// Public so helper classes (<see cref="PortalBlockBase"/>, <see cref="DimensionCommandBuilder"/>)
/// can resolve the facade without referencing the (server-only) <c>ManifoldModSystem</c> type.
/// </remarks>
public static class ManifoldAccess
{
    private static Func<ICoreServerAPI, IManifoldServer?>? _serverResolver;

    /// <summary>
    /// Resolves the server-side Manifold facade for the supplied API.
    /// Returns <c>null</c> if Manifold is not loaded or not initialised.
    /// </summary>
    /// <param name="sapi">Server API context.</param>
    /// <returns>The facade, or <c>null</c>.</returns>
    public static IManifoldServer? GetServer(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        return _serverResolver?.Invoke(sapi);
    }

    /// <summary>
    /// Installs the resolver. Called by <c>ManifoldModSystem</c>; not for consumer use.
    /// </summary>
    /// <param name="resolver">Resolver delegate, or <c>null</c> to clear.</param>
    internal static void SetServerResolver(Func<ICoreServerAPI, IManifoldServer?>? resolver) =>
        _serverResolver = resolver;
}
