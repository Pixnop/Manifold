using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Manifold.Api.Events;

/// <summary>
/// Raised on the server after a non-player entity has been re-homed into another dimension by
/// <see cref="Server.ITransitionService.TeleportEntity"/>. Fires only on success; an exception
/// from the underlying move suppresses the event.
/// </summary>
/// <remarks>
/// For players the engine already exposes <c>IEventAPI.PlayerDimensionChanged</c>; this event
/// is the non-player counterpart and never fires for <c>EntityPlayer</c>.
/// </remarks>
public sealed class EntityChangedDimensionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="EntityChangedDimensionEventArgs"/> class.</summary>
    /// <param name="entity">The entity that was moved.</param>
    /// <param name="previousDimension">Dimension the entity came from.</param>
    /// <param name="newDimension">Dimension the entity is now in.</param>
    /// <param name="newPosition">Final block position in the new dimension (dimension-encoded).</param>
    public EntityChangedDimensionEventArgs(
        Entity entity,
        IDimension previousDimension,
        IDimension newDimension,
        BlockPos newPosition)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(previousDimension);
        ArgumentNullException.ThrowIfNull(newDimension);
        ArgumentNullException.ThrowIfNull(newPosition);
        Entity = entity;
        PreviousDimension = previousDimension;
        NewDimension = newDimension;
        NewPosition = newPosition;
    }

    /// <summary>Entity that was moved. Already re-homed at the time the event fires.</summary>
    public Entity Entity { get; }

    /// <summary>Dimension the entity came from.</summary>
    public IDimension PreviousDimension { get; }

    /// <summary>Dimension the entity is now in.</summary>
    public IDimension NewDimension { get; }

    /// <summary>Final landing position in <see cref="NewDimension"/>.</summary>
    public BlockPos NewPosition { get; }
}
