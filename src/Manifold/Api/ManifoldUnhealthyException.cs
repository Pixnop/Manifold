namespace Manifold.Api;

/// <summary>
/// No longer thrown. Kept for compatibility with mods compiled against earlier versions that
/// catch it.
/// </summary>
public sealed class ManifoldUnhealthyException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="ManifoldUnhealthyException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public ManifoldUnhealthyException(string message)
        : base(message)
    {
    }
}
