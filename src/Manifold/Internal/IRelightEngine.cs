using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// The engine operations <see cref="BlockLightRestorer"/> needs, behind a seam so its pending and
/// retry logic can be tested without a running server. All positions are dimension-encoded; chunk
/// coordinates are local to their dimension (no chunk-Y dimension offset).
/// </summary>
internal interface IRelightEngine
{
    /// <summary>
    /// Runs the engine's synchronous <c>FullRelight</c> over the box, without resending anything,
    /// and marks the chunks it touched as modified so the recomputed light is saved.
    /// </summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    /// <returns><c>false</c> if the engine threw (already logged).</returns>
    bool FullRelight(BlockPos min, BlockPos max);

    /// <summary>
    /// Lists every position that may hold a light source in the chunks a <c>FullRelight</c> of the
    /// box touched, plus one more ring of chunks (sources there shine into the cleared ones).
    /// Best-effort: a chunk that cannot be read is skipped and logged.
    /// </summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    /// <returns>Candidate positions; some may turn out not to emit light.</returns>
    IReadOnlyList<BlockPos> FindLightSources(BlockPos min, BlockPos max);

    /// <summary>Whether the engine currently accepts block-light work for a chunk column.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    /// <returns><c>true</c> if the overworld map chunk at that X/Z is loaded.</returns>
    bool IsGateOpen(int chunkX, int chunkZ);

    /// <summary>Queues the engine's block-light computation for the light source at <paramref name="pos"/>.</summary>
    /// <param name="pos">Position of the source.</param>
    /// <returns><c>false</c> if there is no light-emitting block there (nothing was queued).</returns>
    bool QueueBlockLight(BlockPos pos);

    /// <summary>Whether a light-emitting block is at <paramref name="pos"/>.</summary>
    /// <param name="pos">Position to look at.</param>
    /// <returns><c>true</c> if a block there (solid or fluid layer) emits light.</returns>
    bool EmitsLight(BlockPos pos);

    /// <summary>Whether the engine has computed block light at <paramref name="pos"/>.</summary>
    /// <param name="pos">Position of a light source.</param>
    /// <returns><c>true</c> if its block light level is above zero, or its chunk is no longer loaded.</returns>
    bool IsLit(BlockPos pos);

    /// <summary>
    /// Adds to <paramref name="chunks"/> the loaded chunks a <c>FullRelight</c> of the box touches
    /// (the box plus one chunk around it).
    /// </summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    /// <param name="chunks">Set to add chunk coordinates to.</param>
    void CollectAffectedChunks(BlockPos min, BlockPos max, ISet<(int Cx, int Cy, int Cz)> chunks);

    /// <summary>Resends chunks to the players who are in that dimension and in range of them.</summary>
    /// <param name="dimId">Engine dimension id.</param>
    /// <param name="chunks">Chunk coordinates, local to the dimension.</param>
    void Resend(int dimId, IReadOnlyCollection<(int Cx, int Cy, int Cz)> chunks);

    /// <summary>Logs a warning.</summary>
    /// <param name="message">The message.</param>
    void Warn(string message);
}
