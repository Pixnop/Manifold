namespace Manifold.Api;

/// <summary>Thrown when the Manifold API is requested before <c>StartServerSide</c> / <c>StartClientSide</c> has completed.</summary>
public sealed class ManifoldNotInitializedException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="ManifoldNotInitializedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public ManifoldNotInitializedException(string message)
        : base(message)
    {
    }
}
