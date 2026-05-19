namespace Manifold.Api;

/// <summary>Thrown when a dynamic dimension is built without an explicit lifetime choice.</summary>
public sealed class DimensionLifetimeUnspecifiedException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionLifetimeUnspecifiedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionLifetimeUnspecifiedException(string message)
        : base(message)
    {
    }
}
