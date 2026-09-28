// Deliberately in this exact namespace, under this exact name: MultiPositionBlockDetector matches
// the legacy door by runtime type name (Vintagestory.GameContent.BlockDoor, VSSurvivalMod) rather
// than a hard reference. This fake exercises that match without needing the real game-content
// assembly. Not to be confused with BlockBehaviorDoor, the modern door mechanism.
namespace Vintagestory.GameContent;

using Vintagestory.API.Common;

/// <summary>Test double sharing the legacy door's full type name; otherwise a plain block.</summary>
internal sealed class BlockDoor : Block
{
}
