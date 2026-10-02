using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Remembers, for each player and each dimension they entered, where they came from, so a transit
/// can send them back there. Persisted in the savegame under its own key, with its own schema
/// version recorded in the <see cref="SchemaSidecar"/> (a build that predates it ignores the key).
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class PlayerOriginStore
{
    /// <summary>Current schema version this build writes and reads via <see cref="ToBytes"/>/<see cref="LoadFromBytes"/>.</summary>
    public const int SchemaVersion = 1;

    private readonly Dictionary<string, OriginEntry> _origins = new();

    /// <summary>Whether the store has unsaved changes since the last <see cref="ClearDirty"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Whether the last <see cref="LoadFromBytes"/> refused a schema version newer than this build
    /// supports. Latched until the next call to <see cref="LoadFromBytes"/>. While <c>true</c>, the
    /// caller must not persist this store's key: the newer blob is preserved elsewhere and must not
    /// be overwritten by this session's empty in-memory origins.
    /// </summary>
    public bool IsVersionRefused { get; private set; }

    /// <summary>Record where a player came from when they entered a dimension, replacing any earlier origin for it.</summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <param name="dimId">Engine id of the dimension the player entered.</param>
    /// <param name="origin">Where they came from.</param>
    public void Record(string playerUid, int dimId, OriginEntry origin)
    {
        _origins[Key(playerUid, dimId)] = origin;
        IsDirty = true;
    }

    /// <summary>Look up where a player came from when they entered a dimension.</summary>
    /// <param name="playerUid">Player unique id.</param>
    /// <param name="dimId">Engine id of the dimension the player entered.</param>
    /// <param name="origin">The recorded origin (default if not found).</param>
    /// <returns><c>true</c> if an origin was recorded.</returns>
    public bool TryGet(string playerUid, int dimId, out OriginEntry origin) =>
        _origins.TryGetValue(Key(playerUid, dimId), out origin);

    /// <summary>
    /// Drops every origin recorded for the given dimension id, and every origin that points to it.
    /// Called when a dimension is destroyed: its engine id is recycled, so neither kind may outlive it.
    /// </summary>
    /// <param name="dimId">Engine dimension id being released.</param>
    public void RemoveDimension(int dimId)
    {
        string suffix = "|" + dimId.ToString(CultureInfo.InvariantCulture);
        var stale = _origins
            .Where(kvp => kvp.Value.SourceId == dimId || kvp.Key.EndsWith(suffix, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in stale)
        {
            _origins.Remove(key);
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
        w.Write(_origins.Count);
        foreach (var (key, o) in _origins)
        {
            w.Write(key);
            w.Write(o.SourceId);
            w.Write(o.SourceCode);
            w.Write(o.DestCode);
            w.Write(o.X);
            w.Write(o.Y);
            w.Write(o.Z);
            w.Write(o.Yaw);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Replace the store contents from a byte array produced by <see cref="ToBytes"/>. Clears the
    /// dirty flag. A <paramref name="version"/> newer than <see cref="SchemaVersion"/> is refused
    /// (the data is not parsed, and <see cref="IsVersionRefused"/> is set, instead of misreading
    /// it), unless <paramref name="data"/> is absent or empty: an unrecognized version with nothing
    /// to refuse reads as an empty store, same as an absent blob.
    /// </summary>
    /// <param name="data">Serialised bytes, or <c>null</c>/empty for an empty store.</param>
    /// <param name="logger">Optional logger used to report corrupt data. <c>null</c> silences the report.</param>
    /// <param name="version">The schema version recorded for this blob (from the sidecar).</param>
    public void LoadFromBytes(byte[]? data, ILogger? logger = null, int version = SchemaVersion)
    {
        _origins.Clear();
        IsVersionRefused = version > SchemaVersion && data is { Length: > 0 };
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
                    _origins[key] = new OriginEntry(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadSingle());
                }
            }
            catch (Exception ex)
            {
                _origins.Clear(); // corrupted, start fresh
                logger?.Warning(
                    "[Manifold] Player origin store is corrupt ({0} bytes): {1}. Starting fresh (return-to-origin memory lost).",
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
