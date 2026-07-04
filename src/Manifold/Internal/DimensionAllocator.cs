using System;
using System.Collections.Generic;
using Manifold.Api;
using Manifold.Internal.Util;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Owns the mapping <see cref="AssetLocation"/> ↔ engine dimension id ∈ [10, 1023].
/// First-fit allocation; freed ids re-enter the pool.
/// </summary>
/// <remarks>Server-side, main thread only. Not thread-safe; callers must synchronise.</remarks>
internal sealed class DimensionAllocator
{
    /// <summary>Smallest dimension id available to mods (vanilla reserves 0..9).</summary>
    internal const int MinModId = 10;

    /// <summary>Largest dimension id (10-bit ChunkIndex3D field limit).</summary>
    internal const int MaxModId = 1023;

    private readonly Dictionary<AssetLocation, int> _byCode = new();
    private readonly Dictionary<int, AssetLocation> _byId = new();

    /// <summary>
    /// Allocates or reuses the dimension id for <paramref name="code"/>.
    /// If the code is already registered, returns the existing id (idempotent).
    /// Otherwise allocates the smallest unused id in [MinModId..MaxModId].
    /// </summary>
    /// <param name="code">The asset location code to reserve.</param>
    /// <returns>The dimension id reserved for the code.</returns>
    /// <exception cref="DimensionCapacityExceededException">All mod-available ids are in use.</exception>
    public int Reserve(AssetLocation code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (_byCode.TryGetValue(code, out var existing))
        {
            return existing;
        }

        for (int id = MinModId; id <= MaxModId; id++)
        {
            if (!_byId.ContainsKey(id))
            {
                _byCode[code] = id;
                _byId[id] = code;
                return id;
            }
        }

        throw new DimensionCapacityExceededException(
            $"All mod-available dimension ids ({MinModId}..{MaxModId}) are in use.");
    }

    /// <summary>
    /// Reserves a specific <paramref name="id"/> for <paramref name="code"/>. Used at boot time
    /// when restoring a savegame manifest where ids must be preserved.
    /// Idempotent if the same (code, id) pair is already reserved.
    /// </summary>
    /// <param name="code">The asset location code to reserve.</param>
    /// <param name="id">The specific dimension id to reserve.</param>
    /// <exception cref="System.ArgumentOutOfRangeException">id outside [MinModId, MaxModId].</exception>
    /// <exception cref="DimensionAlreadyRegisteredException">id is reserved by a different code.</exception>
    public void ReserveSpecific(AssetLocation code, int id)
    {
        ArgumentNullException.ThrowIfNull(code);
        Guards.InRange(id, MinModId, MaxModId, nameof(id));

        if (_byId.TryGetValue(id, out var existingCode))
        {
            if (existingCode.Equals(code))
            {
                return;
            }

            throw new DimensionAlreadyRegisteredException(
                $"Dimension id {id} is already reserved by code '{existingCode}'.");
        }

        _byCode[code] = id;
        _byId[id] = code;
    }

    /// <summary>Frees a previously reserved id and its code mapping. No-op if the id is unknown.</summary>
    /// <param name="id">The dimension id to release.</param>
    public void Release(int id)
    {
        if (_byId.TryGetValue(id, out var code))
        {
            _byId.Remove(id);
            _byCode.Remove(code);
        }
    }

    /// <summary>Reverse lookup: id → code.</summary>
    /// <param name="id">The dimension id to look up.</param>
    /// <param name="code">The asset location code, or null if not found.</param>
    /// <returns><c>true</c> if the id is reserved.</returns>
    public bool TryGetCode(int id, out AssetLocation? code)
    {
        if (_byId.TryGetValue(id, out var c))
        {
            code = c;
            return true;
        }

        code = null;
        return false;
    }

    /// <summary>Forward lookup: code → id.</summary>
    /// <param name="code">The asset location code to look up.</param>
    /// <param name="id">The dimension id, or 0 if not found.</param>
    /// <returns><c>true</c> if the code is reserved.</returns>
    public bool TryGetId(AssetLocation code, out int id)
    {
        if (code is not null && _byCode.TryGetValue(code, out var i))
        {
            id = i;
            return true;
        }

        id = 0;
        return false;
    }
}
