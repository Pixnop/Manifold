using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace Manifold.Internal.HarmonyPatches;

/// <summary>
/// Applies Manifold's Harmony patches at boot and tracks whether they succeeded.
/// </summary>
/// <remarks>
/// <para>
/// As of v0 (per Phase 9 research, 2026-05-19), Manifold ships with <b>zero</b>
/// Harmony patches — every gap identified at design time is closeable via public
/// VS API. <c>PatchAll</c> still runs to remain forward-compatible if a future
/// patch is added to the assembly.
/// </para>
/// <para>
/// One concession to internal API access remains: <see cref="WorldgenDispatcher"/>
/// reads the current dimension from <c>IChunkColumnGenerateRequest</c> via reflection
/// because the engine doesn't expose it on the public interface. That's a reflection
/// cast, not a Harmony patch — see <see cref="WorldgenDispatcher.ResolveDimensionFromRequest"/>.
/// </para>
/// <para>Server-side. Apply once in <c>StartServerSide</c>, dispose at shutdown / hot-reload.</para>
/// </remarks>
internal sealed class HarmonyPatcher : IDisposable
{
    /// <summary>Unique identifier for Manifold's Harmony instance.</summary>
    internal const string HarmonyId = "com.fieve.manifold";

    private readonly ILogger _logger;
    private Harmony? _harmony;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="HarmonyPatcher"/> class.</summary>
    /// <param name="logger">Mod logger for diagnostic output.</param>
    public HarmonyPatcher(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a value indicating whether patches applied successfully (or trivially, when there are none).
    /// <c>false</c> if <see cref="Apply"/> threw — services should refuse mutations.
    /// </summary>
    public bool IsHealthy { get; private set; }

    /// <summary>Apply all <c>[HarmonyPatch]</c>-annotated classes in this assembly.</summary>
    /// <remarks>Sets <see cref="IsHealthy"/> based on outcome; logs Error on failure.</remarks>
    public void Apply()
    {
        try
        {
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            IsHealthy = true;
            _logger.Notification("[Manifold] Harmony ready ({0} patches active).", CountPatched());
        }
        catch (Exception ex)
        {
            IsHealthy = false;
            _logger.Error("[Manifold] Harmony patches failed to apply: {0}", ex);
        }
    }

    /// <summary>Unpatch on dispose / hot-reload to keep the next instance clean.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch
        {
            // Swallow on shutdown — Harmony may already be torn down.
        }
    }

    private int CountPatched()
    {
        if (_harmony is null)
        {
            return 0;
        }

        int n = 0;
        foreach (var method in _harmony.GetPatchedMethods())
        {
            _ = method;
            n++;
        }

        return n;
    }
}
