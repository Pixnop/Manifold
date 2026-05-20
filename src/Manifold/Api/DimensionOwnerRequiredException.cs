namespace Manifold.Api;

/// <summary>
/// Thrown when <see cref="Server.IDimensionRegistry.Define"/> is called on an unscoped registry
/// that cannot determine the owning mod id. Use
/// <c>sapi.GetManifoldServer(thisModSystem).Registry.Define(code)</c> instead.
/// </summary>
public sealed class DimensionOwnerRequiredException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionOwnerRequiredException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionOwnerRequiredException(string message)
        : base(message)
    {
    }
}
