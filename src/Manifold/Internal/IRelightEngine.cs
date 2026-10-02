using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace Manifold.Internal;

/// <summary>
/// The engine operations <see cref="BlockLightRestorer"/> needs, behind a seam so its pending and
/// retry logic can be tested without a running server. All positions are dimension-encoded.
/// </summary>
internal interface IRelightEngine
{
    /// <summary>Runs the engine's synchronous <c>FullRelight</c> over the box, without resending anything.</summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    /// <returns><c>false</c> if the engine threw (already logged).</returns>
    bool FullRelight(BlockPos min, BlockPos max);

    /// <summary>
    /// Lists every position that may hold a light source in the chunks a <c>FullRelight</c> of the
    /// box touched, plus one more ring of chunks (sources there shine into the cleared ones).
    /// </summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    /// <returns>Candidate positions; some may turn out not to emit light.</returns>
    IReadOnlyList<BlockPos> FindLightSources(BlockPos min, BlockPos max);

    /// <summary>Whether the engine currently accepts block-light work for the column of <paramref name="pos"/>.</summary>
    /// <param name="pos">Any position in the column.</param>
    /// <returns><c>true</c> if the overworld map chunk at the same X/Z is loaded.</returns>
    bool IsGateOpen(BlockPos pos);

    /// <summary>Queues the engine's block-light computation for the light source at <paramref name="pos"/>.</summary>
    /// <param name="pos">Position of the source.</param>
    /// <returns><c>false</c> if there is no light-emitting block there (nothing was queued).</returns>
    bool QueueBlockLight(BlockPos pos);

    /// <summary>Whether the engine has computed block light at <paramref name="pos"/>.</summary>
    /// <param name="pos">Position of a light source.</param>
    /// <returns><c>true</c> if its block light level is above zero, or its chunk is no longer loaded.</returns>
    bool IsLit(BlockPos pos);

    /// <summary>Resends the chunks a <c>FullRelight</c> of the box touched to the clients in range.</summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    void Broadcast(BlockPos min, BlockPos max);

    /// <summary>Logs a warning.</summary>
    /// <param name="message">The message.</param>
    void Warn(string message);
}
