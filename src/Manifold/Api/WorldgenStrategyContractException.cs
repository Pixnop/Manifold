using System;

namespace Manifold.Api;

/// <summary>Thrown by <c>RegisterStatic</c>/<c>Create</c> when no strategy was attached via <c>WithWorldgen</c>.</summary>
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
