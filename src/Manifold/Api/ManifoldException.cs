using System;

namespace Manifold.Api;

/// <summary>Base type for all Manifold-specific runtime faults.</summary>
public abstract class ManifoldException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ManifoldException"/> class.</summary>
    /// <param name="message">The error message.</param>
    protected ManifoldException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ManifoldException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    protected ManifoldException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}
