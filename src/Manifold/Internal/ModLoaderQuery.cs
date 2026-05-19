using System;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>Production <see cref="IModLoaderQuery"/> backed by <see cref="IModLoader"/>.</summary>
internal sealed class ModLoaderQuery : IModLoaderQuery
{
    private readonly IModLoader _modLoader;

    /// <summary>Initializes a new instance of the <see cref="ModLoaderQuery"/> class.</summary>
    /// <param name="modLoader">VS mod loader instance.</param>
    public ModLoaderQuery(IModLoader modLoader)
    {
        _modLoader = modLoader ?? throw new ArgumentNullException(nameof(modLoader));
    }

    /// <inheritdoc/>
    public bool IsModLoaded(string modId) => _modLoader.IsModEnabled(modId);
}
