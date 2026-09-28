// Deliberately in this exact namespace, under this exact name: MultiPositionBlockDetector matches
// beds by runtime type name (Vintagestory.GameContent.BlockBed, VSSurvivalMod) rather than a hard
// reference, since Manifold's compile-time dependency is VintagestoryAPI only. This fake exercises
// that match without needing the real game-content assembly.
namespace Vintagestory.GameContent;

using Vintagestory.API.Common;

/// <summary>Test double sharing the vanilla bed block's full type name; otherwise a plain block.</summary>
internal class BlockBed : Block
{
}
