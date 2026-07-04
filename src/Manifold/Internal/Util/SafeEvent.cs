using System;

namespace Manifold.Internal.Util;

/// <summary>
/// Raises an <see cref="EventHandler{TArgs}"/> with per-subscriber isolation.
/// </summary>
/// <remarks>
/// Manifold is a shared library: several independent mods subscribe to the same registry and
/// transit events. A bare <c>handler?.Invoke(...)</c> lets one throwing third-party subscriber
/// abort the operation for the initiator and for every other subscriber. This helper invokes each
/// subscriber under its own try/catch so one fault is reported and swallowed instead of propagating
/// out of a core mutation/transit path. Subscribers still run in registration order, and a later
/// subscriber can still mutate the args (e.g. set a cancel flag) even if an earlier one threw.
/// </remarks>
internal static class SafeEvent
{
    /// <summary>Invokes every subscriber of <paramref name="handler"/>, isolating exceptions.</summary>
    /// <typeparam name="TArgs">Event args type.</typeparam>
    /// <param name="handler">The event handler (may be <c>null</c> when there are no subscribers).</param>
    /// <param name="sender">The event sender.</param>
    /// <param name="args">The event args passed to every subscriber.</param>
    /// <param name="onError">Optional callback invoked once per subscriber that throws.</param>
    public static void Raise<TArgs>(
        EventHandler<TArgs>? handler,
        object sender,
        TArgs args,
        Action<Exception>? onError = null)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)d).Invoke(sender, args);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }
}
