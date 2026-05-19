using Manifold.Api;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Snapshot of a finalised builder state, handed to the registry's completion callback.
/// </summary>
/// <param name="Code">The dimension code.</param>
/// <param name="Worldgen">The attached worldgen strategy.</param>
/// <param name="Lifetime">The chosen lifetime.</param>
/// <param name="OwnerModId">The owning mod id.</param>
/// <param name="IsStaticRegistration">True if RegisterStatic was called; false for Create.</param>
internal readonly record struct DimensionBuildRequest(
    AssetLocation Code,
    IWorldgenStrategy Worldgen,
    DimensionLifetime Lifetime,
    string OwnerModId,
    bool IsStaticRegistration);
