using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Remembers each player's last position within each dimension, so the
/// <see cref="Manifold.Api.Transitions.SpawnBehavior.LastVisited"/> spawn behavior can return
/// them there. Persisted in the savegame.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class PlayerPositionStore
{
    /// <summary>Current schema version this build writes and reads via <see cref="ToBytes"/>/<see cref="LoadFromBytes"/>.</summary>
    public const int SchemaVersion = 1;

    private readonly Dictionary<string, (int X, int Y, int Z)> _positions = new();

    /// <summary>Whether the store has unsaved changes since the last <see cref="ClearDirty"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Whether the last <see cref="LoadFromBytes"/> refused a schema version newer than this build
    /// supports. Latched until the next call to <see cref="LoadFromBytes"/>. While <c>true</c>, the
    /// caller must not persist this store's key: the newer blob is preserved elsewhere and must not
    /// be overwritten by this session's empty in-memory positions.
    /// </summary>
    public bool IsVersionRefused { get; private set; }

    /// <summary>Record a player's position within a dimension.</summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y (dimension-local).</param>
    /// <param name="z">World Z.</param>
    public void Record(string playerUid, int dimId, int x, int y, int z)
    {
        _positions[Key(playerUid, dimId)] = (x, y, z);
        IsDirty = true;
    }

    /// <summary>Look up a player's last recorded position in a dimension.</summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="x">Recorded X (0 if not found).</param>
    /// <param name="y">Recorded Y (0 if not found).</param>
    /// <param name="z">Recorded Z (0 if not found).</param>
    /// <returns><c>true</c> if a position was recorded.</returns>
    public bool TryGet(string playerUid, int dimId, out int x, out int y, out int z)
    {
        if (_positions.TryGetValue(Key(playerUid, dimId), out var p))
        {
            (x, y, z) = p;
            return true;
        }

        x = y = z = 0;
        return false;
    }

    /// <summary>
    /// Drops every recorded position for the given dimension id. Called when a dimension is
    /// destroyed so a later dimension reusing the same engine id does not inherit stale
    /// LastVisited coordinates, and so the store does not grow unbounded over a session.
    /// </summary>
    /// <param name="dimId">Engine dimension id being released.</param>
    public void RemoveDimension(int dimId)
    {
        string suffix = "|" + dimId.ToString(CultureInfo.InvariantCulture);
        var stale = _positions.Keys.Where(k => k.EndsWith(suffix, StringComparison.Ordinal)).ToList();
        foreach (var key in stale)
        {
            _positions.Remove(key);
        }

        if (stale.Count > 0)
        {
            IsDirty = true;
        }
    }

    /// <summary>Serialise the store to a byte array.</summary>
    /// <returns>Serialised bytes.</returns>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(_positions.Count);
        foreach (var kvp in _positions)
        {
            w.Write(kvp.Key);
            w.Write(kvp.Value.X);
            w.Write(kvp.Value.Y);
            w.Write(kvp.Value.Z);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Replace the store contents from a byte array produced by <see cref="ToBytes"/>. Clears the
    /// dirty flag. The blob format itself never changed by versioning: a <paramref name="version"/>
    /// newer than <see cref="SchemaVersion"/> is refused - the data is not parsed, and
    /// <see cref="IsVersionRefused"/> is set instead of misreading it.
    /// </summary>
    /// <param name="data">Serialised bytes, or <c>null</c>/empty for an empty store.</param>
    /// <param name="logger">Optional logger used to report corrupt data. <c>null</c> silences the report.</param>
    /// <param name="version">The schema version recorded for this blob (from the sidecar; 1 if it has none).</param>
    public void LoadFromBytes(byte[]? data, ILogger? logger = null, int version = SchemaVersion)
    {
        _positions.Clear();
        IsVersionRefused = version > SchemaVersion;
        if (!IsVersionRefused && data is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms, Encoding.UTF8);
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = r.ReadString();
                    int x = r.ReadInt32();
                    int y = r.ReadInt32();
                    int z = r.ReadInt32();
                    _positions[key] = (x, y, z);
                }
            }
            catch (Exception ex)
            {
                _positions.Clear(); // corrupted - start fresh
                logger?.Warning(
                    "[Manifold] Player position store is corrupt ({0} bytes): {1}. Starting fresh (LastVisited memory lost).",
                    data.Length,
                    ex.Message);
            }
        }

        IsDirty = false;
    }

    /// <summary>Clear the dirty flag after a successful save.</summary>
    public void ClearDirty() => IsDirty = false;

    private static string Key(string playerUid, int dimId) =>
        playerUid + "|" + dimId.ToString(CultureInfo.InvariantCulture);
}
