namespace Manifold.Api;

/// <summary>Thrown when an operation is invalid for the dimension's current state (e.g., transit to a Quarantined dim).</summary>
public sealed class DimensionStateException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionStateException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionStateException(string message)
        : base(message)
    {
    }
}
