// Deliberately in this exact namespace, under this exact name: MultiPositionBlockDetector reads the
// large gear filler's controller position from a block entity with this full type name
// (Vintagestory.GameContent.Mechanics.BEMPMultiblock, VSSurvivalMod) by reflection, since Manifold
// does not reference the assembly that declares it. This fake exercises that read without needing
// the real game-content assembly.
namespace Vintagestory.GameContent.Mechanics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

/// <summary>Test double sharing the large gear filler block entity's full type name and its
/// <see cref="Principal"/> property.</summary>
internal sealed class BEMPMultiblock : BlockEntity
{
    public BlockPos? Principal { get; set; }
}
