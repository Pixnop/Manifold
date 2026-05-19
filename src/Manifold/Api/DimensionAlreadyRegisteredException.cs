namespace Manifold.Api;

/// <summary>Thrown when registering a dimension whose code is already known to the registry in the current boot.</summary>
public sealed class DimensionAlreadyRegisteredException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionAlreadyRegisteredException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionAlreadyRegisteredException(string message)
        : base(message)
    {
    }
}
