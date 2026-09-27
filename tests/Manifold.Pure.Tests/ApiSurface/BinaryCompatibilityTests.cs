using System;
using Manifold.Api.Transitions;
using Xunit;

namespace Manifold.Pure.Tests.ApiSurface;

/// <summary>
/// Members that mods already compiled against an older Manifold call by signature. Removing one
/// compiles fine here but makes those mods throw <see cref="MissingMethodException"/> at runtime.
/// </summary>
public sealed class BinaryCompatibilityTests
{
    [Fact]
    public void TransitionOptions_Should_Declare_A_Parameterless_Constructor()
    {
        // A struct only gets a .ctor() in metadata when it declares one; `new TransitionOptions { ... }`
        // in a mod built against 0.4.1 calls it.
        Assert.NotNull(typeof(TransitionOptions).GetConstructor(Type.EmptyTypes));

        var options = new TransitionOptions();
        Assert.Null(options.SpawnBehavior);
    }

    [Fact]
    public void TransitionOptions_Should_Keep_PreserveInventory_Settable()
    {
        var property = typeof(TransitionOptions).GetProperty("PreserveInventory");

        Assert.NotNull(property?.GetMethod);
        Assert.NotNull(property?.SetMethod);

#pragma warning disable CS0618 // the obsolete member is what old mods call
        var options = new TransitionOptions { PreserveInventory = true };
        Assert.True(options.PreserveInventory);
#pragma warning restore CS0618
    }
}
