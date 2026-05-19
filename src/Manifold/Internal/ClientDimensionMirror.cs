using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Manifold.Api;
using Manifold.Internal.Networking;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Client-side read-only view of dimensions known to the server.
/// Populated by network packet handlers; exposes a snapshot-immutable read API.
/// </summary>
/// <remarks>Client-side. Main thread for mutations (packet handlers run there).</remarks>
internal sealed class ClientDimensionMirror
{
    private volatile ImmutableDictionary<AssetLocation, DimensionImpl> _snapshot =
        ImmutableDictionary<AssetLocation, DimensionImpl>.Empty;

    /// <summary>Raised after a dimension has been added to the mirror.</summary>
    public event Action<IDimension>? Added;

    /// <summary>Raised after a dimension has been removed from the mirror.</summary>
    public event Action<IDimension>? Removed;

    /// <summary>Current snapshot of mirrored dimensions.</summary>
    public IReadOnlyCollection<IDimension> All
    {
        get
        {
            var list = new List<IDimension>(_snapshot.Count);
            foreach (var dim in _snapshot.Values)
            {
                list.Add(dim);
            }

            return list.AsReadOnly();
        }
    }

    /// <summary>Find a mirrored dimension by code.</summary>
    /// <param name="code">Asset code.</param>
    /// <returns>The dimension or <c>null</c>.</returns>
    public IDimension? Get(AssetLocation code) =>
        code is not null && _snapshot.TryGetValue(code, out var dim) ? dim : null;

    /// <summary>Replace the entire mirror with the supplied snapshot.</summary>
    /// <param name="packet">Snapshot packet from server.</param>
    public void ApplyManifest(ManifestSnapshotPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var builder = ImmutableDictionary.CreateBuilder<AssetLocation, DimensionImpl>();
        foreach (var desc in packet.Dimensions)
        {
            var impl = DimensionDescriptorMapper.ToImpl(desc);
            builder[impl.Code] = impl;
        }

        _snapshot = builder.ToImmutable();
    }

    /// <summary>Add (or replace) a single dimension entry.</summary>
    /// <param name="packet">Add packet from server.</param>
    public void ApplyAdded(DimensionAddedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var impl = DimensionDescriptorMapper.ToImpl(packet.Dimension);
        _snapshot = _snapshot.SetItem(impl.Code, impl);
        Added?.Invoke(impl);
    }

    /// <summary>Remove a dimension entry. No-op if the code is unknown.</summary>
    /// <param name="packet">Remove packet from server.</param>
    public void ApplyRemoved(DimensionRemovedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var code = new AssetLocation(packet.Code);
        if (_snapshot.TryGetValue(code, out var existing))
        {
            _snapshot = _snapshot.Remove(code);
            Removed?.Invoke(existing);
        }
    }
}
