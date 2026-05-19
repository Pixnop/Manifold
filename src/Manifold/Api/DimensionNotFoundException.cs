namespace Manifold.Api;

/// <summary>Thrown when a query or operation targets a dimension code that does not exist.</summary>
public sealed class DimensionNotFoundException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionNotFoundException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionNotFoundException(string message)
        : base(message)
    {
    }
}
