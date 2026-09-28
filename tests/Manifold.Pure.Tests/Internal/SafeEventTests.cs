using System;
using System.Collections.Generic;
using Manifold.Internal.Util;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class SafeEventTests
{
    private static readonly string[] ExpectedCallOrder = { "a", "b" };

    [Fact]
    public void Raise_Should_Invoke_All_Subscribers()
    {
        var calls = new List<string>();
        EventHandler<Args>? handler = null;
        handler += (_, _) => calls.Add("a");
        handler += (_, _) => calls.Add("b");

        SafeEvent.Raise(handler, this, new Args());

        Assert.Equal(ExpectedCallOrder, calls);
    }

    [Fact]
    public void Raise_Should_Not_Propagate_A_Subscriber_Exception()
    {
        EventHandler<Args>? handler = null;
        handler += (_, _) => throw new InvalidOperationException("boom");

        // Must not throw - a misbehaving third-party subscriber cannot abort the operation.
        var thrown = Record.Exception(() => SafeEvent.Raise(handler, this, new Args()));

        Assert.Null(thrown);
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
        bool errorCallbackInvoked = false;

        SafeEvent.Raise<Args>(null, this, new Args(), _ => errorCallbackInvoked = true);

        Assert.False(errorCallbackInvoked);
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
