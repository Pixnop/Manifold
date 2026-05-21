using Manifold.Api;
using Manifold.Api.Transitions;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Snapshot of a finalised builder state, handed to the registry's completion callback.
/// </summary>
/// <param name="Code">The dimension code.</param>
/// <param name="Worldgen">The attached worldgen strategy.</param>
/// <param name="Lifetime">The chosen lifetime.</param>
/// <param name="OwnerModId">The owning mod id.</param>
/// <param name="IsStaticRegistration">True if RegisterStatic was called; false for Create.</param>
/// <param name="GenerationRadius">Generation radius in chunks around the transit target.</param>
/// <param name="SpawnBehavior">How players land when entering this dimension.</param>
/// <param name="SpawnPoint">Fixed spawn point for <see cref="Manifold.Api.Transitions.SpawnBehavior.DimensionSpawn"/>, or null.</param>
/// <param name="ForcedGameMode">Game mode forced on entry, or null to preserve the player's current mode.</param>
/// <param name="StreamingLoadRadius">If set, the dimension streams: chunk radius kept generated around each player. Null = bounded.</param>
/// <param name="RelightHeight">Upper Y bound for the relight pass; content above is under-lit until the engine relights.</param>
internal readonly record struct DimensionBuildRequest(
    AssetLocation Code,
    IWorldgenStrategy Worldgen,
    DimensionLifetime Lifetime,
    string OwnerModId,
    bool IsStaticRegistration,
    int GenerationRadius,
    SpawnBehavior SpawnBehavior,
    BlockPos? SpawnPoint,
    EnumGameMode? ForcedGameMode,
    int? StreamingLoadRadius,
    int RelightHeight);
