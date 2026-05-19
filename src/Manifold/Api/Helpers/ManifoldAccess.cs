using System;
using Manifold.Api.Client;
using Manifold.Api.Server;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace Manifold.Api.Helpers;

/// <summary>
/// Static accessor providing helpers with Manifold facades.
/// Wired by <c>ManifoldModSystem</c> at boot.
/// </summary>
/// <remarks>
/// Public so helper classes (<see cref="PortalBlockBase"/>, <see cref="DimensionCommandBuilder"/>)
/// can resolve the facades without referencing the mod-side <c>ManifoldModSystem</c> type.
/// </remarks>
public static class ManifoldAccess
{
    private static Func<ICoreServerAPI, IManifoldServer?>? _serverResolver;
    private static Func<ICoreClientAPI, IManifoldClient?>? _clientResolver;

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
    /// Resolves the client-side Manifold facade for the supplied API.
    /// Returns <c>null</c> if Manifold is not loaded or not initialised.
    /// </summary>
    /// <param name="capi">Client API context.</param>
    /// <returns>The facade, or <c>null</c>.</returns>
    public static IManifoldClient? GetClient(ICoreClientAPI capi)
    {
        ArgumentNullException.ThrowIfNull(capi);
        return _clientResolver?.Invoke(capi);
    }

    /// <summary>
    /// Installs the server resolver. Called by <c>ManifoldModSystem</c>; not for consumer use.
    /// </summary>
    /// <param name="resolver">Resolver delegate, or <c>null</c> to clear.</param>
    internal static void SetServerResolver(Func<ICoreServerAPI, IManifoldServer?>? resolver) =>
        _serverResolver = resolver;

    /// <summary>
    /// Installs the client resolver. Called by <c>ManifoldModSystem</c>; not for consumer use.
    /// </summary>
    /// <param name="resolver">Resolver delegate, or <c>null</c> to clear.</param>
    internal static void SetClientResolver(Func<ICoreClientAPI, IManifoldClient?>? resolver) =>
        _clientResolver = resolver;
}
