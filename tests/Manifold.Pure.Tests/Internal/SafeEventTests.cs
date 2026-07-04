using System;
using System.Collections.Generic;
using Manifold.Internal.Util;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class SafeEventTests
{
    [Fact]
    public void Raise_Should_Invoke_All_Subscribers()
    {
        var calls = new List<string>();
        EventHandler<Args>? handler = null;
        handler += (_, _) => calls.Add("a");
        handler += (_, _) => calls.Add("b");

        SafeEvent.Raise(handler, this, new Args());

        Assert.Equal(new[] { "a", "b" }, calls);
    }

    [Fact]
    public void Raise_Should_Continue_After_A_Subscriber_Throws()
    {
        bool secondRan = false;
        EventHandler<Args>? handler = null;
        handler += (_, _) => throw new InvalidOperationException("boom");
        handler += (_, _) => secondRan = true;

        SafeEvent.Raise(handler, this, new Args());

        Assert.True(secondRan);
    }

    [Fact]
    public void Raise_Should_Not_Propagate_A_Subscriber_Exception()
    {
        EventHandler<Args>? handler = null;
        handler += (_, _) => throw new InvalidOperationException("boom");

        // Must not throw - a misbehaving third-party subscriber cannot abort the operation.
        SafeEvent.Raise(handler, this, new Args());
    }

    [Fact]
    public void Raise_Should_Report_Each_Failure_To_OnError()
    {
        var errors = new List<Exception>();
        EventHandler<Args>? handler = null;
        handler += (_, _) => throw new InvalidOperationException("one");
        handler += (_, _) => throw new InvalidOperationException("two");

        SafeEvent.Raise(handler, this, new Args(), errors.Add);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Raise_Should_Be_NoOp_When_Handler_Null()
    {
        SafeEvent.Raise<Args>(null, this, new Args());
    }

    [Fact]
    public void Raise_Should_Let_A_Later_Subscriber_Mutate_Args_Even_After_An_Earlier_Throw()
    {
        // Cancellation semantics must survive a throwing subscriber: a good handler that sets the
        // cancel flag still runs even if an earlier handler threw.
        var args = new Args();
        EventHandler<Args>? handler = null;
        handler += (_, _) => throw new InvalidOperationException("boom");
        handler += (_, e) => e.Flag = true;

        SafeEvent.Raise(handler, this, args);

        Assert.True(args.Flag);
    }

    private sealed class Args : EventArgs
    {
        public bool Flag { get; set; }
    }
}
