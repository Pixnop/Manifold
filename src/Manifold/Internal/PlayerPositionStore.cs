using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Manifold.Internal;

/// <summary>
/// Remembers each player's last position within each dimension, so the
/// <see cref="Manifold.Api.Transitions.SpawnBehavior.LastVisited"/> spawn behavior can return
/// them there. Persisted in the savegame.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class PlayerPositionStore
{
    private readonly Dictionary<string, (int X, int Y, int Z)> _positions = new();

    /// <summary>Whether the store has unsaved changes since the last <see cref="ClearDirty"/>.</summary>
    public bool IsDirty { get; private set; }

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

    /// <summary>Replace the store contents from a byte array produced by <see cref="ToBytes"/>. Clears the dirty flag.</summary>
    /// <param name="data">Serialised bytes, or <c>null</c>/empty for an empty store.</param>
    public void LoadFromBytes(byte[]? data)
    {
        _positions.Clear();
        if (data is { Length: > 0 })
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
            catch
            {
                _positions.Clear(); // corrupted - start fresh
            }
        }

        IsDirty = false;
    }

    /// <summary>Clear the dirty flag after a successful save.</summary>
    public void ClearDirty() => IsDirty = false;

    private static string Key(string playerUid, int dimId) =>
        playerUid + "|" + dimId.ToString(CultureInfo.InvariantCulture);
}
