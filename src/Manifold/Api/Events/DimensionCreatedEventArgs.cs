using System;

namespace Manifold.Api.Events;

/// <summary>
/// Raised when a dimension transitions to <see cref="DimensionState.Active"/>.
/// </summary>
public sealed class DimensionCreatedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="DimensionCreatedEventArgs"/> class with the affected dimension.</summary>
    /// <param name="dimension">The newly-active dimension.</param>
    public DimensionCreatedEventArgs(IDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        Dimension = dimension;
    }

    /// <summary>Gets the dimension that became active.</summary>
    public IDimension Dimension { get; }
}
