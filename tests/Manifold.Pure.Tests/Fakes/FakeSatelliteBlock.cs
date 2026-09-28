using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Pure.Tests.Fakes;

/// <summary>
/// Stands in for the engine's <c>BlockMultiblock</c>: a block that implements the core
/// <see cref="IMultiblockOffset"/> marker, reporting a fixed, test-supplied controller position.
/// </summary>
internal sealed class FakeSatelliteBlock : Block, IMultiblockOffset
{
    private readonly BlockPos _controlPos;

    public FakeSatelliteBlock(BlockPos controlPos) => _controlPos = controlPos;

    public BlockPos GetControlBlockPos(BlockPos pos) => _controlPos;
}
