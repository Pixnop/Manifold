using Manifold.Api;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Concrete <see cref="IDimension"/> implementation owned by the registry.
/// Stored in an <c>ImmutableDictionary</c> snapshot so worker threads can read without locks.
/// </summary>
/// <param name="Code">Asset location identifier.</param>
/// <param name="InternalId">Engine dimension id.</param>
/// <param name="IsBuiltIn">True for the built-in overworld.</param>
/// <param name="Lifetime">Lifetime category.</param>
/// <param name="OwnerModId">Owning mod id.</param>
/// <param name="State">Current runtime state.</param>
/// <param name="Worldgen">Attached worldgen strategy (null while Pending/Quarantined).</param>
/// <param name="GenerationRadius">Generation radius in chunks around the transit target.</param>
internal sealed record DimensionImpl(
    AssetLocation Code,
    int InternalId,
    bool IsBuiltIn,
    DimensionLifetime Lifetime,
    string OwnerModId,
    DimensionState State,
    IWorldgenStrategy? Worldgen,
    int GenerationRadius) : IDimension
{
    /// <summary>Return a copy with the supplied <see cref="State"/>.</summary>
    /// <param name="newState">The new state.</param>
    /// <returns>Copy with updated state.</returns>
    public DimensionImpl WithState(DimensionState newState) => this with { State = newState };
}
