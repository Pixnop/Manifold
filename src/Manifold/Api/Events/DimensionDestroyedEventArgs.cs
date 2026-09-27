using System;

namespace Manifold.Api.Events;

/// <summary>
/// Raised when a dimension is removed from the registry. Usually an Ephemeral dimension (the
/// normal case), but the admin purge command can also remove a Persistent or Quarantined one.
/// </summary>
public sealed class DimensionDestroyedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="DimensionDestroyedEventArgs"/> class with the removed dimension.</summary>
    /// <param name="dimension">The removed dimension.</param>
    public DimensionDestroyedEventArgs(IDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        Dimension = dimension;
    }

    /// <summary>Gets the dimension that was removed.</summary>
    public IDimension Dimension { get; }
}
