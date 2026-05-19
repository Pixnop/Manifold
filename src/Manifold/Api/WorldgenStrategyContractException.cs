using System;

namespace Manifold.Api;

/// <summary>Thrown when an <c>IWorldgenStrategy</c> violates its contract (malformed Passes, throws in OnInitialize, etc.).</summary>
public sealed class WorldgenStrategyContractException : ManifoldException
{
    /// <summary>Initializes a new instance of the <see cref="WorldgenStrategyContractException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public WorldgenStrategyContractException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WorldgenStrategyContractException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public WorldgenStrategyContractException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
