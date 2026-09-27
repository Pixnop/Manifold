namespace Manifold.Api;

/// <summary>
/// Thrown when registering a dimension whose code is already registered in the current boot, or
/// whose code is Pending under a different owner mod (a Pending entry can only be completed by the
/// mod it was declared for).
/// </summary>
public sealed class DimensionAlreadyRegisteredException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionAlreadyRegisteredException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionAlreadyRegisteredException(string message)
        : base(message)
    {
    }
}
