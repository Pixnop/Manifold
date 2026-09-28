using Vintagestory.API.Datastructures;

namespace Manifold.Internal;

/// <summary>
/// Sidecar record of the schema version Manifold last wrote for each of its persisted blobs. One
/// instance covers a single storage place: the savegame's own blobs (the dimension manifest, the
/// generated-column set, and the per-player-position store) under savegame key <see cref="Key"/>,
/// or one player's blobs (the inventory profile, the saved pre-forced game mode) under the
/// same-named moddata key on that player.
/// </summary>
/// <remarks>
/// Blob bytes themselves are never touched by versioning: only this side table exists, so an
/// older Manifold build that predates it, or a world it has never run against, has no sidecar at
/// all and every blob reads exactly as it always has. Encoded as a <see cref="TreeAttribute"/>
/// mapping blob key to version (int): the same structure the dimension manifest itself already
/// stores its entries in, so there is no new serialization format to write or maintain, and a
/// corrupt sidecar degrades the same way a corrupt manifest does (parse failure -> empty tree).
/// A blob key absent from the tree, including an absent sidecar altogether, means version 1:
/// every blob written before this sidecar existed.
/// </remarks>
internal sealed class SchemaSidecar
{
    /// <summary>Storage key for the sidecar itself, in both the savegame and player moddata.</summary>
    public const string Key = "manifold:schema";

    private readonly TreeAttribute _tree;

    private SchemaSidecar(TreeAttribute tree)
    {
        _tree = tree;
    }

    /// <summary>Whether the sidecar currently records no entries at all.</summary>
    public bool IsEmpty => _tree.Count == 0;

    /// <summary>
    /// Loads a sidecar from its stored bytes, or an empty one (every key reads as version 1) if
    /// the bytes are absent or corrupt.
    /// </summary>
    /// <param name="data">Sidecar bytes previously produced by <see cref="ToBytes"/>, or <c>null</c>.</param>
    /// <returns>The loaded sidecar.</returns>
    public static SchemaSidecar Load(byte[]? data)
    {
        var tree = new TreeAttribute();
        if (data is { Length: > 0 })
        {
            try
            {
                tree.FromBytes(data);
            }
            catch
            {
                // Corrupt sidecar: fall back to the same default every blob key already has when
                // the sidecar is simply absent (version 1), rather than throwing this away with it.
                tree = new TreeAttribute();
            }
        }

        return new SchemaSidecar(tree);
    }

    /// <summary>The schema version last recorded for a blob key.</summary>
    /// <param name="blobKey">The blob's storage key.</param>
    /// <returns>The recorded version, or 1 if the sidecar has no entry for it.</returns>
    public int GetVersion(string blobKey) => _tree.GetInt(blobKey, 1);

    /// <summary>Records the schema version just written for a blob key.</summary>
    /// <param name="blobKey">The blob's storage key.</param>
    /// <param name="version">The version to record.</param>
    public void SetVersion(string blobKey, int version) => _tree.SetInt(blobKey, version);

    /// <summary>
    /// Drops the recorded version for a blob key (it will read back as version 1). Used when the
    /// blob itself is removed, so no stale entry lingers for a key that no longer exists.
    /// </summary>
    /// <param name="blobKey">The blob's storage key.</param>
    public void RemoveVersion(string blobKey) => _tree.RemoveAttribute(blobKey);

    /// <summary>Serializes the sidecar for storage.</summary>
    /// <returns>Serialized bytes.</returns>
    public byte[] ToBytes() => _tree.ToBytes();
}
