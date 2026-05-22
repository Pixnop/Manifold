using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Manifold.Api;

namespace Manifold.Internal;

/// <summary>
/// One player's per-dimension inventory profiles: a serialized snapshot per (category, owner key)
/// plus the owner key the physical inventory currently holds per category. Serialized to and from the
/// player's moddata so it is saved together with the physical inventory.
/// </summary>
/// <remarks>Server-side, main thread. Snapshot payloads are opaque bytes (produced by the swapper).</remarks>
internal sealed class PlayerInventoryStore
{
    private readonly Dictionary<string, byte[]> _snapshots = new();
    private readonly Dictionary<ManifoldInventory, string> _currentKeys = new();

    /// <summary>The owner key the physical inventory currently holds for a category (shared if unset).</summary>
    /// <param name="category">The inventory category to query.</param>
    /// <returns>The stored owner key, or <see cref="InventoryProfileResolver.SharedKey"/> if none is recorded.</returns>
    public string CurrentKey(ManifoldInventory category) =>
        _currentKeys.TryGetValue(category, out var k) ? k : InventoryProfileResolver.SharedKey;

    /// <summary>Records the owner key the physical inventory now holds for a category.</summary>
    /// <param name="category">The inventory category.</param>
    /// <param name="ownerKey">The new owner key.</param>
    public void SetCurrentKey(ManifoldInventory category, string ownerKey) => _currentKeys[category] = ownerKey;

    /// <summary>Whether a snapshot exists for (category, owner key).</summary>
    /// <param name="category">The inventory category.</param>
    /// <param name="ownerKey">The owner key.</param>
    /// <returns><c>true</c> if a snapshot is stored for this pair.</returns>
    public bool HasSnapshot(ManifoldInventory category, string ownerKey) =>
        _snapshots.ContainsKey(SnapshotKey(category, ownerKey));

    /// <summary>Returns the snapshot bytes for (category, owner key), or null.</summary>
    /// <param name="category">The inventory category.</param>
    /// <param name="ownerKey">The owner key.</param>
    /// <returns>The stored bytes, or <c>null</c> if absent.</returns>
    public byte[]? GetSnapshot(ManifoldInventory category, string ownerKey) =>
        _snapshots.TryGetValue(SnapshotKey(category, ownerKey), out var b) ? b : null;

    /// <summary>Stores the snapshot bytes for (category, owner key).</summary>
    /// <param name="category">The inventory category.</param>
    /// <param name="ownerKey">The owner key.</param>
    /// <param name="bytes">The serialized inventory bytes to store.</param>
    public void SetSnapshot(ManifoldInventory category, string ownerKey, byte[] bytes) =>
        _snapshots[SnapshotKey(category, ownerKey)] = bytes;

    /// <summary>Serialise the whole store for player moddata.</summary>
    /// <returns>Serialised bytes.</returns>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(_currentKeys.Count);
        foreach (var kvp in _currentKeys)
        {
            w.Write((int)kvp.Key);
            w.Write(kvp.Value);
        }

        w.Write(_snapshots.Count);
        foreach (var kvp in _snapshots)
        {
            w.Write(kvp.Key);
            w.Write(kvp.Value.Length);
            w.Write(kvp.Value);
        }

        return ms.ToArray();
    }

    /// <summary>Deserialise a store from moddata bytes; returns an empty store on null/empty/corrupt input.</summary>
    /// <param name="data">Serialised bytes, or <c>null</c>/empty for an empty store.</param>
    /// <returns>The deserialised store, or a fresh empty store if input is absent or corrupt.</returns>
    public static PlayerInventoryStore FromBytes(byte[]? data)
    {
        var store = new PlayerInventoryStore();
        if (data is not { Length: > 0 })
        {
            return store;
        }

        try
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            int keyCount = r.ReadInt32();
            for (int i = 0; i < keyCount; i++)
            {
                var category = (ManifoldInventory)r.ReadInt32();
                store._currentKeys[category] = r.ReadString();
            }

            int snapCount = r.ReadInt32();
            for (int i = 0; i < snapCount; i++)
            {
                string key = r.ReadString();
                int len = r.ReadInt32();
                store._snapshots[key] = r.ReadBytes(len);
            }
        }
        catch
        {
            return new PlayerInventoryStore();
        }

        return store;
    }

    private static string SnapshotKey(ManifoldInventory category, string ownerKey) =>
        ((int)category).ToString(CultureInfo.InvariantCulture) + "|" + ownerKey;
}
