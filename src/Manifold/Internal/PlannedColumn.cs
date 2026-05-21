using System.Collections.Generic;

namespace Manifold.Internal;

/// <summary>A column the driver should ensure this tick, with the players that need it sent.</summary>
/// <param name="DimId">Engine dimension id.</param>
/// <param name="Cx">Chunk X.</param>
/// <param name="Cz">Chunk Z.</param>
/// <param name="PlayerUids">Uids of players whose window contains this column.</param>
internal readonly record struct PlannedColumn(int DimId, int Cx, int Cz, IReadOnlyList<string> PlayerUids);
