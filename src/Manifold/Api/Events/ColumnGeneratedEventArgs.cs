using System;
using Vintagestory.API.Common;

namespace Manifold.Api.Events;

/// <summary>
/// Raised after Manifold generates a brand-new chunk column - never for a column it only loaded
/// from disk. See <see cref="Server.IDimensionRegistry.ColumnGenerated"/>.
/// </summary>
public sealed class ColumnGeneratedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ColumnGeneratedEventArgs"/> class.</summary>
    /// <param name="dimension">The dimension the column was generated in.</param>
    /// <param name="chunkX">Chunk-grid X of the generated column.</param>
    /// <param name="chunkZ">Chunk-grid Z of the generated column.</param>
    /// <param name="blockAccessor">Accessor for decorating the column.</param>
    public ColumnGeneratedEventArgs(IDimension dimension, int chunkX, int chunkZ, IBlockAccessor blockAccessor)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blockAccessor);
        Dimension = dimension;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        BlockAccessor = blockAccessor;
    }

    /// <summary>Gets the dimension the column was generated in.</summary>
    public IDimension Dimension { get; }

    /// <summary>Gets the chunk-grid X of the generated column.</summary>
    public int ChunkX { get; }

    /// <summary>Gets the chunk-grid Z of the generated column.</summary>
    public int ChunkZ { get; }

    /// <summary>
    /// Gets the accessor to use for decorating the generated column (placing a structure, a marker,
    /// loot). This is the world's plain, non-bulk accessor: writes apply immediately, no
    /// <c>Commit</c> call is needed. The column has not been sent to any client yet when this event
    /// fires - sending always happens afterwards - so a block set here reaches clients as part of
    /// the column's normal first send, with no extra resync required.
    /// </summary>
    public IBlockAccessor BlockAccessor { get; }
}
