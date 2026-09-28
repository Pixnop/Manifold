// Deliberately in this exact namespace, under this exact name: MultiPositionBlockDetector matches
// the large gear filler by runtime type name (Vintagestory.GameContent.Mechanics.BlockMPMultiblockGear,
// VSSurvivalMod) rather than a hard reference. This fake exercises that match without needing the
// real game-content assembly.
namespace Vintagestory.GameContent.Mechanics;

using Vintagestory.API.Common;

/// <summary>Test double sharing the large gear filler's full type name; otherwise a plain block.</summary>
internal sealed class BlockMPMultiblockGear : Block
{
}
