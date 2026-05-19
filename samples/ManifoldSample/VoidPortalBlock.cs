using Manifold.Api.Helpers;
using Vintagestory.API.Common;

namespace ManifoldSample;

/// <summary>Portal block that teleports the colliding player to the manifoldsample:void dimension.</summary>
public sealed class VoidPortalBlock : PortalBlockBase
{
    /// <inheritdoc/>
    protected override AssetLocation TargetDimensionCode { get; } =
        new("manifoldsample", "void");
}
