using Manifold.Api;
using Vintagestory.API.Common;

namespace Manifold.Internal.Networking;

/// <summary>
/// Bidirectional conversion between <see cref="DimensionImpl"/> (server) and <see cref="DimensionDescriptor"/> (wire).
/// </summary>
internal static class DimensionDescriptorMapper
{
    /// <summary>Server → wire.</summary>
    /// <param name="dim">Server-side dimension.</param>
    /// <returns>Wire descriptor.</returns>
    public static DimensionDescriptor ToDescriptor(IDimension dim) => new()
    {
        Code = dim.Code.ToString(),
        InternalId = dim.InternalId,
        IsBuiltIn = dim.IsBuiltIn,
        Lifetime = (int)dim.Lifetime,
        OwnerModId = dim.OwnerModId,
        State = (int)dim.State,
    };

    /// <summary>Wire → client-side <see cref="DimensionImpl"/> (Worldgen is null on client).</summary>
    /// <param name="d">Wire descriptor.</param>
    /// <returns>Client-side dimension record.</returns>
    public static DimensionImpl ToImpl(DimensionDescriptor d) => new(
        Code: new AssetLocation(d.Code),
        InternalId: d.InternalId,
        IsBuiltIn: d.IsBuiltIn,
        Lifetime: (DimensionLifetime)d.Lifetime,
        OwnerModId: d.OwnerModId,
        State: (DimensionState)d.State,
        Worldgen: null,
        GenerationRadius: DimensionBuilderImpl.DefaultGenerationRadius,
        SpawnBehavior: Api.Transitions.SpawnBehavior.SameCoordinates,
        SpawnPoint: null,
        ForcedGameMode: null,
        StreamingLoadRadius: null,
        RelightHeight: DimensionBuilderImpl.DefaultRelightHeight);
}
