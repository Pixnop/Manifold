using System;
using Manifold.Api.Events;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Manifold.Api.Server;

/// <summary>Primitive transit operations between dimensions.</summary>
/// <remarks>Server-side. All members must be invoked on the main thread.</remarks>
public interface ITransitionService
{
    /// <summary>Raised before transit completes; set <c>Cancel = true</c> to abort.</summary>
    event EventHandler<PlayerEnteringDimensionEventArgs> PlayerEntering;

    /// <summary>
    /// Raised after the destination region has been generated but before the actual teleport.
    /// Cancellable: setting <c>Cancel = true</c> aborts the transit and leaves the player in the
    /// source dimension. Fires only on the player-transit path (<see cref="TeleportPlayer"/>).
    /// </summary>
    event EventHandler<PlayerArrivingDimensionEventArgs> PlayerArriving;

    /// <summary>Raised after the player has entered the target dimension.</summary>
    event EventHandler<PlayerEnteredDimensionEventArgs> PlayerEntered;

    /// <summary>Raised after the player has left the source dimension.</summary>
    event EventHandler<PlayerLeftDimensionEventArgs> PlayerLeft;

    /// <summary>
    /// Raised after a non-player entity has been successfully moved to another dimension by
    /// <see cref="TeleportEntity"/>. The engine's <c>PlayerDimensionChanged</c> covers players;
    /// this event covers everything else and never fires for <c>EntityPlayer</c>.
    /// </summary>
    event EventHandler<EntityChangedDimensionEventArgs> EntityChangedDimension;

    /// <summary>
    /// Teleport a player to the dimension identified by <paramref name="targetDim"/>.
    /// Raises <see cref="PlayerEntering"/> (cancellable, pre-generation), then <see cref="PlayerArriving"/>
    /// (cancellable, post-generation), then <see cref="PlayerLeft"/> and <see cref="PlayerEntered"/>.
    /// If the player is riding a mount (a boat, a saddled creature, any <c>IMountableSeat</c>), they
    /// are cleanly dismounted right before the move: the mount is left behind in the source
    /// dimension, never dragged along and never left desynced with a rider that changed dimension
    /// out from under it. If the mount's seat refuses to release them, the whole transit is
    /// silently aborted (see <see cref="TryTeleportPlayer"/> to detect this).
    /// </summary>
    /// <param name="player">Server player to teleport.</param>
    /// <param name="targetDim">Target dimension code.</param>
    /// <param name="options">Optional transit settings.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="player"/> or <paramref name="targetDim"/> is null.</exception>
    /// <exception cref="Manifold.Api.DimensionNotFoundException">Target code unknown.</exception>
    /// <exception cref="Manifold.Api.DimensionStateException">Target is not Active.</exception>
    void TeleportPlayer(IServerPlayer player, AssetLocation targetDim, TransitionOptions options = default);

    /// <summary>
    /// Same transit as <see cref="TeleportPlayer"/>, but returns whether the player actually moved
    /// (same events, same generation, same landing-position resolution, same mount handling) instead
    /// of leaving a cancelled transit indistinguishable from a completed one.
    /// </summary>
    /// <param name="player">Server player to teleport.</param>
    /// <param name="targetDim">Target dimension code.</param>
    /// <param name="options">Optional transit settings.</param>
    /// <returns>
    /// <c>true</c> if the player was moved; <c>false</c> if a <see cref="PlayerEntering"/> or
    /// <see cref="PlayerArriving"/> subscriber cancelled the transit, or if the player was riding a
    /// mount whose seat refused to release them (nothing moves in that case either).
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="player"/> or <paramref name="targetDim"/> is null.</exception>
    /// <exception cref="Manifold.Api.DimensionNotFoundException">Target code unknown.</exception>
    /// <exception cref="Manifold.Api.DimensionStateException">Target is not Active.</exception>
    bool TryTeleportPlayer(IServerPlayer player, AssetLocation targetDim, TransitionOptions options = default);

    /// <summary>
    /// Moves a non-player entity (item, mob) to another dimension. Generates the destination region if
    /// needed, re-homes the entity, then raises <see cref="EntityChangedDimension"/>. For players use
    /// <see cref="TeleportPlayer"/> instead.
    /// </summary>
    /// <param name="entity">The non-player entity to move.</param>
    /// <param name="targetDim">Destination dimension code.</param>
    /// <param name="options">Optional position override / resolver.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> or <paramref name="targetDim"/> is null.</exception>
    /// <exception cref="System.ArgumentException">The entity is a player.</exception>
    /// <exception cref="DimensionNotFoundException">No dimension with that code.</exception>
    /// <exception cref="DimensionStateException">The destination is not active.</exception>
    void TeleportEntity(Entity entity, AssetLocation targetDim, TransitionOptions options = default);

    /// <summary>
    /// Moves a single block plus its <c>BlockEntity</c> state (inventory, attributes, BE-behaviors)
    /// from <paramref name="source"/> in any dimension to <paramref name="targetLocal"/> in
    /// <paramref name="targetDim"/>. The destination region is generated on demand, then the block
    /// is serialized via <c>BlockEntity.ToTreeAttributes</c> at the source, set at the target, and
    /// rehydrated via <c>FromTreeAttributes</c>; finally the source slot is set to air. The source
    /// dimension is taken from <c>source.dimension</c>; the target dimension overrides
    /// <c>targetLocal.dimension</c>.
    /// </summary>
    /// <remarks>
    /// Refuses (returns <c>false</c>, without touching either position) when the source is one part
    /// of a structure spanning several grid positions: a multiblock satellite or controller (this
    /// also covers ordinary doors and trapdoors, which are two cells tall), or one half of a bed.
    /// Moving only one such position with this method's plain block-plus-<c>BlockEntity</c> copy
    /// would leave the structure broken both at the source and at the target. The refusal is logged
    /// as a warning with the reason; call <see cref="IsMultiPositionBlock"/> beforehand to detect it
    /// without attempting the move (for example to give a player a clearer message).
    /// </remarks>
    /// <param name="source">Source position. <see cref="Vintagestory.API.MathTools.BlockPos.dimension"/> is the source dim.</param>
    /// <param name="targetDim">Target dimension code.</param>
    /// <param name="targetLocal">Target position; the dimension field is rewritten to the target.</param>
    /// <returns>
    /// <c>true</c> when a non-air block was moved; <c>false</c> when the source slot was air, or when
    /// the source is part of a multi-position structure (see <see cref="IsMultiPositionBlock"/>).
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// <paramref name="source"/>, <paramref name="targetDim"/>, or <paramref name="targetLocal"/> is null.
    /// </exception>
    /// <exception cref="DimensionNotFoundException">Target code unknown.</exception>
    /// <exception cref="DimensionStateException">Target is not Active.</exception>
    bool TeleportBlock(BlockPos source, AssetLocation targetDim, BlockPos targetLocal);

    /// <summary>
    /// True when the block at <paramref name="pos"/> is one part of a structure spanning several
    /// grid positions - a multiblock satellite or controller (including a door or trapdoor cell
    /// beyond its first), or one half of a bed - and would therefore be refused by
    /// <see cref="TeleportBlock"/> rather than moved. Safe to call ahead of time; reads world state
    /// only and does not require <c>pos.dimension</c> to be a registered, active dimension.
    /// </summary>
    /// <param name="pos">Dimension-encoded position to inspect.</param>
    /// <returns><c>true</c> when <see cref="TeleportBlock"/> would refuse this position.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="pos"/> is null.</exception>
    bool IsMultiPositionBlock(BlockPos pos);
}
