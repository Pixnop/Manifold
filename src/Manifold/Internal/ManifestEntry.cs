using Manifold.Api;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Frozen snapshot of one dimension's identity, persisted in the savegame manifest.
/// </summary>
/// <param name="Code">Asset location identifier.</param>
/// <param name="InternalId">Engine dimension id.</param>
/// <param name="Lifetime">Lifetime category.</param>
/// <param name="OwnerModId">Owning mod id.</param>
internal readonly record struct ManifestEntry(
    AssetLocation Code,
    int InternalId,
    DimensionLifetime Lifetime,
    string OwnerModId);
