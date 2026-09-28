// Deliberately in this exact namespace, under this exact name: MultiPositionBlockDetector matches
// the large trough by runtime type name (Vintagestory.GameContent.BlockTroughDoubleBlock,
// VSSurvivalMod) rather than a hard reference. This fake exercises that match without needing the
// real game-content assembly.
namespace Vintagestory.GameContent;

using Vintagestory.API.Common;

/// <summary>Test double sharing the vanilla large trough's full type name; otherwise a plain block.</summary>
internal sealed class BlockTroughDoubleBlock : Block
{
}
