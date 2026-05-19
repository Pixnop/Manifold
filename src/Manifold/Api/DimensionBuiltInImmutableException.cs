namespace Manifold.Api;

/// <summary>Thrown when a caller attempts to mutate or remove the built-in overworld.</summary>
public sealed class DimensionBuiltInImmutableException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionBuiltInImmutableException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionBuiltInImmutableException(string message)
        : base(message)
    {
    }
}
