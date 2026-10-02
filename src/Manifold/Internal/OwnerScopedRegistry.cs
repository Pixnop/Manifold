using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Events;
using Manifold.Api.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Manifold.Internal;

/// <summary>
/// An <see cref="IDimensionRegistry"/> view bound to a specific consumer mod id, so that
/// <see cref="Define"/> records the correct owner. Returned by
/// <c>sapi.GetManifoldServer(ModSystem)</c>. All other operations delegate to the shared registry.
/// </summary>
internal sealed class OwnerScopedRegistry : IDimensionRegistry
{
    private readonly DimensionRegistry _shared;
    private readonly string _ownerModId;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnerScopedRegistry"/> class.
    /// </summary>
    /// <param name="shared">The shared dimension registry.</param>
    /// <param name="ownerModId">The mod id that owns dimensions registered through this view.</param>
    public OwnerScopedRegistry(DimensionRegistry shared, string ownerModId)
    {
        _shared = shared ?? throw new ArgumentNullException(nameof(shared));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModId);
        _ownerModId = ownerModId;
    }

    /// <inheritdoc/>
    public event EventHandler<DimensionCreatedEventArgs> Created
    {
        add => _shared.Created += value;
        remove => _shared.Created -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<DimensionDestroyedEventArgs> Destroyed
    {
        add => _shared.Destroyed += value;
        remove => _shared.Destroyed -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<ColumnGeneratedEventArgs> ColumnGenerated
    {
        add => _shared.ColumnGenerated += value;
        remove => _shared.ColumnGenerated -= value;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IDimension> All => _shared.All;

    /// <inheritdoc/>
    public IDimension? Get(AssetLocation code) => _shared.Get(code);

    /// <inheritdoc/>
    /// <remarks>Delegates to <see cref="DimensionRegistry.DefineForOwner"/> using the bound owner mod id.</remarks>
    public IDimensionBuilder Define(AssetLocation code) => _shared.DefineForOwner(code, _ownerModId);

    /// <inheritdoc/>
    public bool TryRemove(AssetLocation code) => _shared.TryRemove(code);

    /// <inheritdoc/>
    public IDimension? GetByInternalId(int internalId) => ((IDimensionRegistry)_shared).GetByInternalId(internalId);

    /// <inheritdoc/>
    public IDimension? GetDimensionOf(Entity entity) => _shared.GetDimensionOf(entity);
}
