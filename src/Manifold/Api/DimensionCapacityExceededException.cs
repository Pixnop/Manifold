namespace Manifold.Api;

/// <summary>Thrown when all 1014 mod-available dimension IDs (10..1023) are in use.</summary>
public sealed class DimensionCapacityExceededException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="DimensionCapacityExceededException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DimensionCapacityExceededException(string message)
        : base(message)
    {
    }
}
