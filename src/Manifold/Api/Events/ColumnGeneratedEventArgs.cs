using System;
using Vintagestory.API.Common;

namespace Manifold.Api.Events;

/// <summary>
/// Raised after Manifold generates a brand-new chunk column; never for a column it only loaded
/// from disk. See <see cref="Server.IDimensionRegistry.ColumnGenerated"/>.
/// </summary>
public sealed class ColumnGeneratedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ColumnGeneratedEventArgs"/> class.</summary>
    /// <param name="dimension">The dimension the column was generated in.</param>
    /// <param name="chunkX">Chunk-grid X of the generated column.</param>
    /// <param name="chunkZ">Chunk-grid Z of the generated column.</param>
    /// <param name="blockAccessor">Accessor for decorating the column.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dimension"/> or <paramref name="blockAccessor"/> is null.
    /// </exception>
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
    /// loot). This is a plain, non-bulk accessor: writes apply immediately, no <c>Commit</c> call is
    /// needed. It is built with <c>synchronize:false, relight:false</c> (the same semantics as
    /// worldgen itself), so a write here does not queue a server relight task or a
    /// neighbour-update/resync entry the way a live player edit would; use
    /// <see cref="Server.IManifoldServer.RelightRegion"/> if the decoration needs lighting. The
    /// column has not been sent to any client yet when this event fires (sending always happens
    /// afterwards), so a block set here reaches clients as part of the column's normal first send,
    /// with no extra resync required. Only this event's column (<see cref="ChunkX"/>,
    /// <see cref="ChunkZ"/>) is guaranteed loaded: a write that lands in a neighbour column not
    /// generated yet is silently dropped, so a structure spanning multiple columns must be split
    /// and placed per column.
    /// </summary>
    public IBlockAccessor BlockAccessor { get; }
}
