using System;
using Manifold.Api;
using Manifold.Api.Client;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.ApiSurface;

/// <summary>
/// Tests for <see cref="CoreServerApiExtensions.GetManifoldServer(ICoreServerAPI)"/> and
/// <see cref="CoreClientApiExtensions.GetManifoldClient"/> when Manifold's ModSystem has not (yet)
/// wired a resolver into <see cref="ManifoldAccess"/> - the first error a consumer mod sees if it is
/// missing the 'manifold' modinfo dependency.
/// </summary>
/// <remarks>
/// <see cref="ManifoldAccess"/> is process-wide static state, so this class gets its own collection
/// (no parallel run against another test touching it) and resets both resolvers on every dispose.
/// </remarks>
[Collection("ManifoldAccess")]
public sealed class ManifoldAccessExtensionsTests : IDisposable
{
    public void Dispose()
    {
        ManifoldAccess.SetServerResolver(null);
        ManifoldAccess.SetClientResolver(null);
    }

    [Fact]
    public void GetManifoldServer_Should_Throw_When_Not_Wired()
    {
        var sapi = Substitute.For<ICoreServerAPI>();
        Assert.Throws<ManifoldNotInitializedException>(() => sapi.GetManifoldServer());
    }

    [Fact]
    public void GetManifoldClient_Should_Throw_When_Not_Wired()
    {
        var capi = Substitute.For<ICoreClientAPI>();
        Assert.Throws<ManifoldNotInitializedException>(() => capi.GetManifoldClient());
    }
}
