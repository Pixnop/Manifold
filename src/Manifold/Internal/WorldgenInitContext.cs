using Manifold.Api.Worldgen;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Concrete <see cref="IWorldgenInitContext"/>.</summary>
/// <param name="DimensionId">Engine dimension id.</param>
/// <param name="Seed">World seed.</param>
/// <param name="Api">Server API.</param>
internal sealed record WorldgenInitContext(int DimensionId, int Seed, ICoreServerAPI Api) : IWorldgenInitContext;
