using Manifold.Api.Worldgen;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Concrete <see cref="IWorldgenInitContext"/>.</summary>
internal sealed class WorldgenInitContext : IWorldgenInitContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldgenInitContext"/> class.
    /// </summary>
    /// <param name="dimensionId">Engine dimension id.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="api">Server API.</param>
    public WorldgenInitContext(int dimensionId, int seed, ICoreServerAPI api)
    {
        DimensionId = dimensionId;
        Seed = seed;
        Api = api;
    }

    /// <inheritdoc/>
    public int DimensionId { get; }

    /// <inheritdoc/>
    public int Seed { get; }

    /// <inheritdoc/>
    public ICoreServerAPI Api { get; }
}
