using System;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>Production <see cref="IManifestStore"/> backed by the VS savegame data store.</summary>
internal sealed class SaveGameManifestStore : IManifestStore
{
    private readonly ICoreServerAPI _sapi;

    /// <summary>Initializes a new instance of the <see cref="SaveGameManifestStore"/> class.</summary>
    /// <param name="sapi">Server API; must have <c>WorldManager.SaveGame</c> available.</param>
    public SaveGameManifestStore(ICoreServerAPI sapi)
    {
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
    }

    /// <inheritdoc/>
    public byte[]? Read(string key) => _sapi.WorldManager.SaveGame.GetData(key);

    /// <inheritdoc/>
    public void Write(string key, byte[] data) => _sapi.WorldManager.SaveGame.StoreData(key, data);
}
