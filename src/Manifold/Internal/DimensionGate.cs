using Manifold.Api;
using Manifold.Api.Server;
using Vintagestory.API.Common;

namespace Manifold.Internal;

/// <summary>
/// Shared "must be a known, Active dimension" resolution used by transit and by
/// <see cref="ManifoldServerFacade.GenerateRegion"/>, so both refuse an unknown or non-Active target
/// the same way instead of each keeping its own copy of the check.
/// </summary>
internal static class DimensionGate
{
    /// <summary>Resolves <paramref name="code"/> to a registered dimension and requires it to be Active.</summary>
    /// <param name="registry">Registry to resolve against.</param>
    /// <param name="code">Dimension code to resolve.</param>
    /// <returns>The resolved, Active dimension.</returns>
    /// <exception cref="DimensionNotFoundException">No dimension is registered under <paramref name="code"/>.</exception>
    /// <exception cref="DimensionStateException"><paramref name="code"/> is registered but not Active.</exception>
    public static IDimension RequireActive(IDimensionRegistry registry, AssetLocation code)
    {
        var dim = registry.Get(code)
            ?? throw new DimensionNotFoundException($"No dimension registered with code '{code}'.");
        if (dim.State != DimensionState.Active)
        {
            throw new DimensionStateException($"Dimension '{code}' is in state {dim.State}; transit not allowed.");
        }

        return dim;
    }
}
