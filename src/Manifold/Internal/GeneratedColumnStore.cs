using System;
using System.Collections.Generic;

namespace Manifold.Internal;

/// <summary>
/// Tracks which dimension chunk columns Manifold has already generated, so that on a later
/// visit (especially after a server restart, when chunks live on disk but are not yet loaded
/// in memory) the generator <em>loads</em> the persisted column instead of regenerating it -
/// which would overwrite player modifications.
/// </summary>
/// <remarks>
/// <para>
/// Server-side, main thread. The engine offers no synchronous on-disk chunk-existence check
/// (<c>GetChunk</c> is in-memory only; <c>TestChunkExists</c> is async), so Manifold maintains
/// its own persisted set.
/// </para>
/// <para>
/// Keys pack (dimension, chunkX, chunkZ) into a single <see cref="long"/>:
/// 10 bits dimension (0..1023) | 21 bits chunkX | 21 bits chunkZ. Chunk coordinates fit in
/// 21 bits because the world is at most 2^21 chunks wide.
/// </para>
/// </remarks>
internal sealed class GeneratedColumnStore
{
    private const int CoordBits = 21;
    private const long CoordMask = (1L << CoordBits) - 1;

    private readonly HashSet<long> _keys = new();

    /// <summary>Whether the set has unsaved changes since the last <see cref="ClearDirty"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Returns <c>true</c> if the column has already been generated and persisted.</summary>
    /// <param name="dim">Engine dimension id.</param>
    /// <param name="cx">Chunk X.</param>
    /// <param name="cz">Chunk Z.</param>
    /// <returns><c>true</c> if previously generated.</returns>
    public bool IsGenerated(int dim, int cx, int cz) => _keys.Contains(Pack(dim, cx, cz));

    /// <summary>Records that a column has been generated. Sets <see cref="IsDirty"/> if newly added.</summary>
    /// <param name="dim">Engine dimension id.</param>
    /// <param name="cx">Chunk X.</param>
    /// <param name="cz">Chunk Z.</param>
    public void MarkGenerated(int dim, int cx, int cz)
    {
        if (_keys.Add(Pack(dim, cx, cz)))
        {
            IsDirty = true;
        }
    }

    /// <summary>Serialises the set to a byte array (8 bytes per key, little-endian).</summary>
    /// <returns>Serialised bytes.</returns>
    public byte[] ToBytes()
    {
        var buffer = new byte[_keys.Count * sizeof(long)];
        int offset = 0;
        foreach (var key in _keys)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), key);
            offset += sizeof(long);
        }

        return buffer;
    }

    /// <summary>Replaces the set from a byte array produced by <see cref="ToBytes"/>. Clears the dirty flag.</summary>
    /// <param name="data">Serialised bytes, or <c>null</c>/empty for an empty set.</param>
    public void LoadFromBytes(byte[]? data)
    {
        _keys.Clear();
        if (data is not null && data.Length >= sizeof(long))
        {
            int count = data.Length / sizeof(long);
            for (int i = 0; i < count; i++)
            {
                _keys.Add(BitConverter.ToInt64(data, i * sizeof(long)));
            }
        }

        IsDirty = false;
    }

    /// <summary>Clears the dirty flag after a successful save.</summary>
    public void ClearDirty() => IsDirty = false;

    private static long Pack(int dim, int cx, int cz) =>
        ((long)(dim & 0x3FF) << (CoordBits * 2))
        | ((cx & CoordMask) << CoordBits)
        | (cz & CoordMask);
}
