using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Detects a block that is one part of a structure spanning several grid positions: a multiblock
/// satellite or controller, or one half of a bed. Moving only one such position with a plain
/// block-plus-<c>BlockEntity</c> copy (see <see cref="IBlockMover"/>) would leave the structure
/// broken both at the old position and the new one, so <c>TransitService.TeleportBlock</c> refuses
/// the move instead of attempting it.
/// </summary>
/// <remarks>
/// Detection is engine-mechanism-based, not a hardcoded block list, so it also covers modded
/// structures built on the same mechanisms:
/// <list type="bullet">
/// <item>
/// <b>Satellite</b>: the position implements the core <c>Vintagestory.API.Common.IMultiblockOffset</c>
/// interface. The engine's <c>BlockMultiblock</c> (<c>Vintagestory.GameContent</c>, VSEssentials) is
/// the only implementation today, and it is what fills every non-origin cell of a
/// <c>BlockBehaviorMultiblock</c> structure and every door/trapdoor cell beyond its first (width
/// or height greater than one) - so this one check also covers ordinary doors, which are two
/// cells tall.
/// </item>
/// <item>
/// <b>Controller</b>: a bounded neighbour search for a satellite that reports this position as its
/// controller. The search radius matches (and is a safe superset of) the vanilla
/// <c>multiblock-monolithic-*</c> block's declared offset range (<c>game/blocktypes/multiblock.json</c>):
/// dx/dz vary n2..p2 and dy varies n2..p3, so a symmetric radius of 2 horizontally and 3 vertically
/// covers every vanilla offset. A modded satellite offset outside that box will not be found by this
/// search.
/// </item>
/// <item>
/// <b>Bed</b>: the vanilla bed (<c>Vintagestory.GameContent.BlockBed</c>, VSSurvivalMod) links its
/// head/feet halves purely through block code and facing, not through <see cref="IMultiblockOffset"/>,
/// so it needs its own check. Matched by runtime type name rather than a hard reference, because
/// Manifold's compile-time dependency is <c>VintagestoryAPI</c> only (never a specific game-content
/// mod assembly).
/// </item>
/// </list>
/// A block class from a different mod that reproduces the bed pattern (two independently linked
/// cells with no shared marker interface) is not detected; there is no engine-level signal to key
/// on for that case short of the exact vanilla type name.
/// </remarks>
internal static class MultiPositionBlockDetector
{
    /// <summary>Horizontal search radius: the vanilla multiblock filler's dx/dz variants run n2..p2.</summary>
    private const int HorizontalSearchRadius = 2;

    /// <summary>Vertical search radius: the vanilla multiblock filler's dy variant runs n2..p3.</summary>
    private const int VerticalSearchRadius = 3;

    private const string BedBlockTypeName = "Vintagestory.GameContent.BlockBed";

    /// <summary>
    /// True when the block at <paramref name="pos"/> cannot be safely relocated on its own.
    /// </summary>
    /// <param name="accessor">Accessor to read <paramref name="pos"/> and, for the controller check,
    /// its bounded neighbourhood.</param>
    /// <param name="pos">Dimension-encoded position to inspect.</param>
    /// <param name="reason">A human-readable explanation when this returns <c>true</c>; otherwise
    /// <c>null</c>.</param>
    /// <returns><c>true</c> when the position cannot be safely relocated on its own.</returns>
    internal static bool IsMultiPosition(IBlockAccessor accessor, BlockPos pos, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(pos);

        Block block = accessor.GetBlock(pos);

        if (block is IMultiblockOffset satellite)
        {
            reason = $"{block.Code} at {pos} is a satellite of a multiblock structure controlled at {satellite.GetControlBlockPos(pos)}.";
            return true;
        }

        if (string.Equals(block.GetType().FullName, BedBlockTypeName, StringComparison.Ordinal))
        {
            reason = $"{block.Code} at {pos} is one half of a two-block bed.";
            return true;
        }

        if (TryFindControlledSatellite(accessor, pos, out BlockPos? satellitePos))
        {
            reason = $"{block.Code} at {pos} controls a multiblock structure with a dependent block at {satellitePos}.";
            return true;
        }

        reason = null;
        return false;
    }

    /// <summary>
    /// Searches the bounded neighbourhood of <paramref name="pos"/> for a live
    /// <see cref="IMultiblockOffset"/> satellite whose controller is <paramref name="pos"/> itself.
    /// </summary>
    private static bool TryFindControlledSatellite(IBlockAccessor accessor, BlockPos pos, out BlockPos? satellitePos)
    {
        var probe = pos.Copy();
        for (int dy = -VerticalSearchRadius; dy <= VerticalSearchRadius; dy++)
        {
            for (int dx = -HorizontalSearchRadius; dx <= HorizontalSearchRadius; dx++)
            {
                for (int dz = -HorizontalSearchRadius; dz <= HorizontalSearchRadius; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    probe.Set(pos.X + dx, pos.Y + dy, pos.Z + dz);
                    if (accessor.GetBlock(probe) is not IMultiblockOffset offset)
                    {
                        continue;
                    }

                    BlockPos control = offset.GetControlBlockPos(probe);
                    if (control.X == pos.X && control.Y == pos.Y && control.Z == pos.Z)
                    {
                        satellitePos = probe.Copy();
                        return true;
                    }
                }
            }
        }

        satellitePos = null;
        return false;
    }
}
