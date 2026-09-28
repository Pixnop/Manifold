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
    private static IManifoldServer? _server;
    private static IManifoldClient? _client;

    /// <summary>
    /// Resolves the server-side Manifold facade for the supplied API.
    /// Returns <c>null</c> if Manifold is not loaded or not initialised.
    /// </summary>
    /// <param name="sapi">Server API context.</param>
    /// <returns>The facade, or <c>null</c>.</returns>
    public static IManifoldServer? GetServer(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        return _server;
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
        return _client;
    }

    /// <summary>
    /// Installs the server facade. Called by <c>ManifoldModSystem</c>; not for consumer use.
    /// </summary>
    /// <param name="server">The facade, or <c>null</c> to clear.</param>
    internal static void SetServerResolver(IManifoldServer? server) => _server = server;

    /// <summary>
    /// Installs the client facade. Called by <c>ManifoldModSystem</c>; not for consumer use.
    /// </summary>
    /// <param name="client">The facade, or <c>null</c> to clear.</param>
    internal static void SetClientResolver(IManifoldClient? client) => _client = client;
}
