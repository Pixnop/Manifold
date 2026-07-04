using Vintagestory.API.Server;

namespace Manifold.Api.Worldgen;

/// <summary>
/// Provided to <see cref="IWorldgenStrategy.OnInitialize"/>, which is called once per dimension
/// before its first column is generated.
/// </summary>
/// <remarks>Server-side, main thread. Treat <see cref="Api"/> as read-only and resolve assets only.</remarks>
public interface IWorldgenInitContext
{
    /// <summary>Engine dimension id allocated to the owning dimension.</summary>
    int DimensionId { get; }

    /// <summary>Deterministic world seed.</summary>
    int Seed { get; }

    /// <summary>Server API for resolving block ids and reading static config.</summary>
    ICoreServerAPI Api { get; }
}
