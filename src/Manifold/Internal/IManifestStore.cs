namespace Manifold.Internal;

/// <summary>
/// Abstraction over raw byte-array key/value persistence backed by the savegame.
/// Production impl wraps <c>sapi.WorldManager.SaveGame.GetData/StoreData</c>;
/// tests use an in-memory dictionary.
/// </summary>
internal interface IManifestStore
{
    /// <summary>Read the value stored under <paramref name="key"/>, or <c>null</c> if absent.</summary>
    /// <param name="key">Storage key.</param>
    /// <returns>Stored bytes, or <c>null</c>.</returns>
    byte[]? Read(string key);

    /// <summary>Write the value under <paramref name="key"/>, overwriting any prior data.</summary>
    /// <param name="key">Storage key.</param>
    /// <param name="data">Bytes to store.</param>
    void Write(string key, byte[] data);
}
