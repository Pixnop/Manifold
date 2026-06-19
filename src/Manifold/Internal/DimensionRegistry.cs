using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Api.Server;
using Manifold.Internal.Util;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Source-of-truth registry for dimensions.
/// </summary>
/// <remarks>
/// Server-side. Mutations on main thread only.
/// Reads are wait-free: the snapshot field is replaced atomically (volatile reference).
/// </remarks>
internal sealed class DimensionRegistry : IDimensionRegistry
{
    private static readonly AssetLocation OverworldCode = new("manifold", "overworld");

    private readonly DimensionAllocator _allocator;
    private readonly System.Func<int, bool>? _isOccupied;

    private volatile ImmutableDictionary<AssetLocation, DimensionImpl> _snapshot =
        ImmutableDictionary<AssetLocation, DimensionImpl>.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DimensionRegistry"/> class with the built-in overworld and dependencies.
    /// </summary>
    /// <param name="allocator">Dimension id allocator.</param>
    /// <param name="isOccupied">
    /// Optional predicate that returns <c>true</c> when at least one connected player is currently
    /// inside the dimension with the given engine id. When supplied, <see cref="TryRemove"/> refuses
    /// (returns <c>false</c>) to remove an occupied dimension. <c>null</c> disables the check.
    /// </param>
    public DimensionRegistry(DimensionAllocator allocator, System.Func<int, bool>? isOccupied = null)
    {
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _isOccupied = isOccupied;

        var overworld = new DimensionImpl(
            Code: OverworldCode,
            InternalId: 0,
            IsBuiltIn: true,
            Lifetime: DimensionLifetime.BuiltIn,
            OwnerModId: "manifold",
            State: DimensionState.Active,
            Worldgen: null,
            GenerationRadius: DimensionBuilderImpl.DefaultGenerationRadius,
            SpawnBehavior: Api.Transitions.SpawnBehavior.SameCoordinates,
            SpawnPoint: null,
            ForcedGameMode: null,
            StreamingLoadRadius: null,
            RelightHeight: DimensionBuilderImpl.DefaultRelightHeight,
            SeparateInventory: ManifoldInventory.None,
            Metadata: DimensionBuilderImpl.EmptyMetadata,
            StreamingBudgetPerTick: null,
            SkyCapY: null);
        _snapshot = _snapshot.Add(OverworldCode, overworld);
    }

    /// <inheritdoc/>
    public event EventHandler<DimensionCreatedEventArgs>? Created;

    /// <inheritdoc/>
    public event EventHandler<DimensionDestroyedEventArgs>? Destroyed;

    /// <inheritdoc/>
    public IReadOnlyCollection<IDimension> All => _snapshot.Values.Cast<IDimension>().ToList().AsReadOnly();

    /// <inheritdoc/>
    public IDimension? Get(AssetLocation code) =>
        code is not null && _snapshot.TryGetValue(code, out var dim) ? dim : null;

    /// <inheritdoc/>
    /// <exception cref="DimensionOwnerRequiredException">
    /// Always thrown. Use <see cref="DefineForOwner"/> via
    /// <c>sapi.GetManifoldServer(thisModSystem).Registry</c> so the owning mod id is recorded.
    /// </exception>
    public IDimensionBuilder Define(AssetLocation code) =>
        throw new DimensionOwnerRequiredException(
            "Register dimensions via sapi.GetManifoldServer(thisModSystem).Registry so Manifold "
            + "can record the owning mod id. The parameterless server facade is read/transit only.");

    /// <inheritdoc/>
    public bool TryRemove(AssetLocation code)
    {
        if (code is null || !_snapshot.TryGetValue(code, out var dim))
        {
            return false;
        }

        if (dim.IsBuiltIn || dim.Lifetime == DimensionLifetime.BuiltIn)
        {
            throw new DimensionBuiltInImmutableException(
                $"Dimension '{code}' is built-in and cannot be removed.");
        }

        if (dim.Lifetime == DimensionLifetime.Persistent)
        {
            throw new DimensionStateException(
                $"Dimension '{code}' is persistent; removal requires the admin purge command.");
        }

        // Refuse to destroy a dimension while a player is inside it. The caller must move occupants
        // out first (or use IManifoldServer.ForceRemoveDimension, which evacuates then removes).
        if (_isOccupied is not null && _isOccupied(dim.InternalId))
        {
            return false;
        }

        _snapshot = _snapshot.Remove(code);
        _allocator.Release(dim.InternalId);
        Destroyed?.Invoke(this, new DimensionDestroyedEventArgs(dim));
        return true;
    }

    /// <summary>
    /// Start a fluent declaration with an explicit owner mod id. Called by
    /// <see cref="OwnerScopedRegistry"/> which captures the owner from the caller's
    /// <c>ModSystem.Mod.Info.ModID</c>.
    /// </summary>
    /// <param name="code">The new dimension's code.</param>
    /// <param name="ownerModId">The mod id that owns this dimension.</param>
    /// <returns>A single-use builder.</returns>
    /// <exception cref="DimensionAlreadyRegisteredException">The code is already registered.</exception>
    internal IDimensionBuilder DefineForOwner(AssetLocation code, string ownerModId)
    {
        DimensionCodeValidator.Validate(code);
        Guards.NotNullOrWhiteSpace(ownerModId, nameof(ownerModId));
        if (_snapshot.TryGetValue(code, out var existing))
        {
            if (existing.State != DimensionState.Pending)
            {
                throw new DimensionAlreadyRegisteredException(
                    $"Dimension '{code}' is already registered in this boot.");
            }

            // A Pending entry is owned by a specific mod (recorded in the manifest). Only that owner
            // may complete it - otherwise a second mod could claim another mod's dimension and the
            // promoted record would silently keep the original owner id (broken attribution).
            if (!string.Equals(existing.OwnerModId, ownerModId, StringComparison.Ordinal))
            {
                throw new DimensionAlreadyRegisteredException(
                    $"Dimension '{code}' is pending under owner '{existing.OwnerModId}' and cannot be claimed by '{ownerModId}'.");
            }
        }

        return new DimensionBuilderImpl(code, ownerModId, Complete);
    }

    /// <summary>
    /// Insert a manifest-derived dimension in <see cref="DimensionState.Pending"/> or
    /// <see cref="DimensionState.Quarantined"/>. Used at boot before consumer ModSystems run.
    /// </summary>
    /// <param name="entry">Manifest entry data (Code, InternalId, Lifetime, OwnerModId).</param>
    /// <param name="state">Either Pending or Quarantined.</param>
    /// <exception cref="ArgumentException">state is not Pending or Quarantined.</exception>
    internal void SeedFromManifest(ManifestEntry entry, DimensionState state)
    {
        if (state != DimensionState.Pending && state != DimensionState.Quarantined)
        {
            throw new ArgumentException(
                $"SeedFromManifest only accepts Pending or Quarantined; got {state}.",
                nameof(state));
        }

        if (_snapshot.ContainsKey(entry.Code))
        {
            return;
        }

        _allocator.ReserveSpecific(entry.Code, entry.InternalId);
        var dim = new DimensionImpl(
            Code: entry.Code,
            InternalId: entry.InternalId,
            IsBuiltIn: false,
            Lifetime: entry.Lifetime,
            OwnerModId: entry.OwnerModId,
            State: state,
            Worldgen: null,
            GenerationRadius: DimensionBuilderImpl.DefaultGenerationRadius,
            SpawnBehavior: Api.Transitions.SpawnBehavior.SameCoordinates,
            SpawnPoint: null,
            ForcedGameMode: null,
            StreamingLoadRadius: null,
            RelightHeight: DimensionBuilderImpl.DefaultRelightHeight,
            SeparateInventory: ManifoldInventory.None,
            Metadata: DimensionBuilderImpl.EmptyMetadata,
            StreamingBudgetPerTick: null,
            SkyCapY: null);
        _snapshot = _snapshot.Add(entry.Code, dim);
    }

    /// <summary>Worker-pool safe reverse lookup by engine dimension id.</summary>
    /// <param name="internalId">Engine dimension id.</param>
    /// <returns>The dimension if found; <c>null</c> otherwise.</returns>
    internal DimensionImpl? GetByInternalId(int internalId) =>
        _snapshot.Values.FirstOrDefault(d => d.InternalId == internalId);

    private DimensionImpl Complete(DimensionBuildRequest request)
    {
        if (_snapshot.TryGetValue(request.Code, out var existing) &&
            existing.State == DimensionState.Pending)
        {
            var promoted = existing with
            {
                State = DimensionState.Active,
                Worldgen = request.Worldgen,
                GenerationRadius = request.GenerationRadius,
                SpawnBehavior = request.SpawnBehavior,
                SpawnPoint = request.SpawnPoint,
                ForcedGameMode = request.ForcedGameMode,
                StreamingLoadRadius = request.StreamingLoadRadius,
                RelightHeight = request.RelightHeight,
                SeparateInventory = request.SeparateInventory,
                Metadata = request.Metadata,
                StreamingBudgetPerTick = request.StreamingBudgetPerTick,
                SkyCapY = request.SkyCapY,
            };
            _snapshot = _snapshot.SetItem(request.Code, promoted);
            Created?.Invoke(this, new DimensionCreatedEventArgs(promoted));
            return promoted;
        }

        int id = _allocator.Reserve(request.Code);
        var dim = new DimensionImpl(
            Code: request.Code,
            InternalId: id,
            IsBuiltIn: false,
            Lifetime: request.Lifetime,
            OwnerModId: request.OwnerModId,
            State: DimensionState.Active,
            Worldgen: request.Worldgen,
            GenerationRadius: request.GenerationRadius,
            SpawnBehavior: request.SpawnBehavior,
            SpawnPoint: request.SpawnPoint,
            ForcedGameMode: request.ForcedGameMode,
            StreamingLoadRadius: request.StreamingLoadRadius,
            RelightHeight: request.RelightHeight,
            SeparateInventory: request.SeparateInventory,
            Metadata: request.Metadata,
            StreamingBudgetPerTick: request.StreamingBudgetPerTick,
            SkyCapY: request.SkyCapY);
        _snapshot = _snapshot.Add(request.Code, dim);
        Created?.Invoke(this, new DimensionCreatedEventArgs(dim));
        return dim;
    }
}
