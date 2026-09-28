using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// Detects a block that is one part of a structure spanning several grid positions: a multiblock
/// satellite or controller, or one half of a linked pair such as a bed. Moving only one such
/// position with a plain block-plus-<c>BlockEntity</c> copy (see <see cref="IBlockMover"/>) would
/// leave the structure broken both at the old position and the new one, so
/// <c>TransitService.TeleportBlock</c> refuses the move instead of attempting it.
/// </summary>
/// <remarks>
/// Detection is engine-mechanism-based where a marker interface exists, and falls back to matching
/// known vanilla runtime types (walking the type's base-class chain, so a modded subclass of one of
/// them is still caught) where it does not:
/// <list type="bullet">
/// <item>
/// <b>Satellite</b>: the position implements the core <c>Vintagestory.API.Common.IMultiblockOffset</c>
/// interface. The engine's <c>BlockMultiblock</c> (<c>Vintagestory.GameContent</c>, VSEssentials) is
/// the only implementation today, and it is what fills every non-origin cell of a
/// <c>BlockBehaviorMultiblock</c> structure and every door cell beyond its first (width or height
/// greater than one, including wide gates) - so this one check also covers ordinary doors, which are
/// two cells tall. A vanilla trapdoor is a single cell and is not covered by, or affected by, this
/// check.
/// </item>
/// <item>
/// <b>Large gear filler</b>: <c>BlockLargeGear3m</c> (VSSurvivalMod) surrounds its centre with
/// <c>BlockMPMultiblockGear</c> fillers that do not implement <see cref="IMultiblockOffset"/>; each
/// filler's <c>BEMPMultiblock.Principal</c> block-entity field points at the gear centre instead.
/// Matched by runtime type name on both the block and its block entity, then <c>Principal</c> is read
/// by reflection, because Manifold's compile-time dependency is <c>VintagestoryAPI</c> only (never a
/// specific game-content mod assembly).
/// </item>
/// <item>
/// <b>Controller</b>: a bounded neighbour search for a satellite (either kind above) that reports
/// this position as its controller. The search radius matches (and is a safe superset of) the
/// vanilla <c>multiblock-monolithic-*</c> block's declared offset range
/// (<c>game/blocktypes/multiblock.json</c>): dx/dz vary n2..p2 and dy varies n2..p3, so a symmetric
/// radius of 2 horizontally and 3 vertically covers every vanilla offset, including the large gear's
/// 3x3 footprint. A modded satellite offset outside that box will not be found by this search.
/// </item>
/// <item>
/// <b>Linked pair</b>: some vanilla structures link two positions purely through block code and
/// facing, not through <see cref="IMultiblockOffset"/>, so they need their own check by runtime type
/// name: the bed (<c>Vintagestory.GameContent.BlockBed</c>, VSSurvivalMod), the large trough
/// (<c>Vintagestory.GameContent.BlockTroughDoubleBlock</c>, VSSurvivalMod, head/feet halves), and the
/// legacy door (<c>Vintagestory.GameContent.BlockDoor</c>, VSSurvivalMod, up/down halves - worlds
/// predating the current door behavior may still contain these).
/// </item>
/// </list>
/// A block class from a different mod that reproduces one of these patterns (independently linked
/// cells with no shared marker interface, and no shared base type with a vanilla one above) is not
/// detected; there is no engine-level signal to key on for that case short of the exact vanilla type
/// name or one of its subclasses.
/// </remarks>
internal static class MultiPositionBlockDetector
{
    /// <summary>Horizontal search radius: the vanilla multiblock filler's dx/dz variants run n2..p2.</summary>
    private const int HorizontalSearchRadius = 2;

    /// <summary>Vertical search radius: the vanilla multiblock filler's dy variant runs n2..p3.</summary>
    private const int VerticalSearchRadius = 3;

    private const string GearFillerBlockTypeName = "Vintagestory.GameContent.Mechanics.BlockMPMultiblockGear";

    private const string GearFillerBlockEntityTypeName = "Vintagestory.GameContent.Mechanics.BEMPMultiblock";

    /// <summary>Vanilla block types that link exactly two positions by type and facing alone.</summary>
    private static readonly (string TypeName, string Description)[] LinkedPairBlockTypes =
    [
        ("Vintagestory.GameContent.BlockBed", "bed"),
        ("Vintagestory.GameContent.BlockTroughDoubleBlock", "large trough"),
        ("Vintagestory.GameContent.BlockDoor", "legacy door"),
    ];

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

        if (TryGetSatelliteControlPos(accessor, block, pos, out BlockPos? controlPos))
        {
            reason = $"{block.Code} at {pos} is a satellite of a multiblock structure controlled at {controlPos}.";
            return true;
        }

        foreach ((string typeName, string description) in LinkedPairBlockTypes)
        {
            if (MatchesTypeOrBase(block.GetType(), typeName))
            {
                reason = $"{block.Code} at {pos} is one half of a two-position {description}.";
                return true;
            }
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
    /// True when the block at <paramref name="pos"/> is a satellite - either an
    /// <see cref="IMultiblockOffset"/> implementer or a large gear filler - and reports a
    /// controller position.
    /// </summary>
    private static bool TryGetSatelliteControlPos(IBlockAccessor accessor, Block block, BlockPos pos, out BlockPos? controlPos)
    {
        if (block is IMultiblockOffset offset)
        {
            controlPos = offset.GetControlBlockPos(pos);
            return true;
        }

        if (block is not null && MatchesTypeOrBase(block.GetType(), GearFillerBlockTypeName))
        {
            return TryGetGearFillerPrincipal(accessor, pos, out controlPos);
        }

        controlPos = null;
        return false;
    }

    /// <summary>
    /// Reads the large gear filler's <c>BEMPMultiblock.Principal</c> block-entity field by
    /// reflection, since Manifold does not reference the assembly that declares it.
    /// </summary>
    private static bool TryGetGearFillerPrincipal(IBlockAccessor accessor, BlockPos pos, out BlockPos? controlPos)
    {
        BlockEntity? blockEntity = accessor.GetBlockEntity(pos);
        if (blockEntity is not null
            && MatchesTypeOrBase(blockEntity.GetType(), GearFillerBlockEntityTypeName)
            && blockEntity.GetType().GetProperty("Principal")?.GetValue(blockEntity) is BlockPos principal)
        {
            controlPos = principal;
            return true;
        }

        controlPos = null;
        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/>, or any type in its base-class chain, has the runtime
    /// type name <paramref name="fullName"/>. Walking the chain catches a modded subclass of a
    /// vanilla type matched by name.
    /// </summary>
    private static bool MatchesTypeOrBase(Type type, string fullName)
    {
        for (Type? candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (string.Equals(candidate.FullName, fullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Searches the bounded neighbourhood of <paramref name="pos"/> for a live satellite (either an
    /// <see cref="IMultiblockOffset"/> implementer or a large gear filler) whose controller is
    /// <paramref name="pos"/> itself.
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
                    Block probeBlock = accessor.GetBlock(probe);
                    if (!TryGetSatelliteControlPos(accessor, probeBlock, probe, out BlockPos? control) || control is null)
                    {
                        continue;
                    }

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
