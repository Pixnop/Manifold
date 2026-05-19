namespace Manifold.Api;

/// <summary>Thrown when a mutation is attempted while Manifold is unhealthy (Harmony patch failed at boot).</summary>
public sealed class ManifoldUnhealthyException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="ManifoldUnhealthyException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public ManifoldUnhealthyException(string message)
        : base(message)
    {
    }
}
