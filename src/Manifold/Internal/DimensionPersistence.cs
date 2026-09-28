using System;
using System.Collections.Generic;
using System.Linq;
using Manifold.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Manifold.Internal;

/// <summary>
/// Reads / writes the dimension manifest in the savegame and classifies each entry on boot.
/// </summary>
/// <remarks>Server-side. Main thread + <c>GameWorldSave</c> handler.</remarks>
internal sealed class DimensionPersistence
{
    /// <summary>Storage key for the manifest in the savegame.</summary>
    public const string ManifestKey = "manifold:manifest";

    /// <summary>Current schema version this build writes and reads via <see cref="Save"/>/<see cref="LoadOrEmpty"/>.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Key a refused, unrecognized-version manifest blob is copied to, so it is never lost.</summary>
    public const string UnrecognizedManifestKey = ManifestKey + ".unrecognized";

    private readonly IManifestStore _store;
    private readonly System.Func<string, bool> _isModLoaded;
    private readonly ILogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="DimensionPersistence"/> class.</summary>
    /// <param name="store">Manifest byte store.</param>
    /// <param name="isModLoaded">Mod loader probe for orphan detection (e.g. <c>sapi.ModLoader.IsModEnabled</c>).</param>
    /// <param name="logger">Optional logger used to report a corrupt manifest. <c>null</c> silences the report.</param>
    public DimensionPersistence(IManifestStore store, System.Func<string, bool> isModLoaded, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _isModLoaded = isModLoaded ?? throw new ArgumentNullException(nameof(isModLoaded));
        _logger = logger;
    }

    /// <summary>
    /// Whether the last <see cref="LoadOrEmpty"/> refused a schema version newer than this build
    /// supports. Latched until the next call to <see cref="LoadOrEmpty"/>. While <c>true</c>,
    /// <see cref="Save"/> is a no-op: this session's registrations are incomplete (not every
    /// consumer mod has necessarily re-registered yet), so writing them now would overwrite the
    /// newer manifest already preserved under <see cref="UnrecognizedManifestKey"/> with a partial
    /// (in the extreme, empty) one.
    /// </summary>
    public bool IsVersionRefused { get; private set; }

    /// <summary>
    /// Persist the supplied manifest entries (skipping Ephemeral and BuiltIn). A no-op while
    /// <see cref="IsVersionRefused"/> is set (see its remarks).
    /// </summary>
    /// <param name="entries">Entries to persist.</param>
    public void Save(IEnumerable<ManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (IsVersionRefused)
        {
            return;
        }

        var tree = new TreeAttribute();
        var list = new TreeAttribute();
        int idx = 0;
        foreach (var e in entries)
        {
            if (e.Lifetime == DimensionLifetime.Ephemeral || e.Lifetime == DimensionLifetime.BuiltIn)
            {
                continue;
            }

            var child = new TreeAttribute();
            child.SetString("code", e.Code.ToString());
            child.SetInt("id", e.InternalId);
            child.SetInt("lifetime", (int)e.Lifetime);
            child.SetString("owner", e.OwnerModId);
            var idxStr = idx.ToString(System.Globalization.CultureInfo.InvariantCulture);
            list[idxStr] = (IAttribute)child;
            idx++;
        }

        tree["entries"] = (IAttribute)list;
        var bytes = tree.ToBytes();
        _store.Write(ManifestKey, bytes);
    }

    /// <summary>
    /// Load all manifest entries, or an empty sequence on corruption, missing data, or a
    /// <paramref name="version"/> newer than <see cref="SchemaVersion"/>. A corrupt manifest is
    /// refused the way it always was: logged, dropped, consumers re-register at boot. An
    /// unrecognized-version manifest is refused the same way, but is also copied verbatim to
    /// <see cref="UnrecognizedManifestKey"/> and latches <see cref="IsVersionRefused"/>, which
    /// makes <see cref="Save"/> a no-op for the rest of the session: unlike the corrupt case,
    /// this data is not gone, so it must not be overwritten by an incomplete re-registration pass.
    /// </summary>
    /// <param name="version">The schema version recorded for the manifest (from the sidecar; 1 if it has none).</param>
    /// <returns>Manifest entries (zero or more).</returns>
    public IEnumerable<ManifestEntry> LoadOrEmpty(int version = SchemaVersion)
    {
        IsVersionRefused = false;
        var raw = _store.Read(ManifestKey);
        if (raw is null || raw.Length == 0)
        {
            yield break;
        }

        if (version > SchemaVersion)
        {
            IsVersionRefused = true;
            _store.Write(UnrecognizedManifestKey, raw);
            _logger?.Error(
                "[Manifold] '{0}' is schema version {1}, this build supports up to {2}. Persistent dimension ids will be re-allocated this session; the original blob is preserved under '{3}' and this key will not be re-saved.",
                ManifestKey,
                version,
                SchemaVersion,
                UnrecognizedManifestKey);
            yield break;
        }

        TreeAttribute tree;
        try
        {
            tree = new TreeAttribute();
            tree.FromBytes(raw);
        }
        catch (Exception ex)
        {
            // Corrupted manifest - consumers re-register at boot (Persistent dimension ids are
            // re-allocated), but that recovery is otherwise silent, so log it.
            _logger?.Error(
                "[Manifold] Dimension manifest '{0}' is corrupt ({1} bytes): {2}. Persistent dimension ids will be re-allocated.",
                ManifestKey,
                raw.Length,
                ex.Message);
            yield break;
        }

        var list = tree.GetTreeAttribute("entries");
        if (list is null)
        {
            yield break;
        }

        foreach (var attr in list.Select(kvp => kvp.Value))
        {
            if (attr is not TreeAttribute child)
            {
                continue;
            }

            var codeStr = child.GetString("code");
            if (string.IsNullOrEmpty(codeStr))
            {
                continue;
            }

            yield return new ManifestEntry(
                Code: new AssetLocation(codeStr),
                InternalId: child.GetInt("id"),
                Lifetime: (DimensionLifetime)child.GetInt("lifetime"),
                OwnerModId: child.GetString("owner") ?? string.Empty);
        }
    }

    /// <summary>Classify a manifest entry by whether its owner mod is currently loaded.</summary>
    /// <param name="entry">Entry to classify.</param>
    /// <returns><see cref="DimensionState.Pending"/> if owner loaded; otherwise <see cref="DimensionState.Quarantined"/>.</returns>
    public DimensionState Classify(ManifestEntry entry) =>
        _isModLoaded(entry.OwnerModId)
            ? DimensionState.Pending
            : DimensionState.Quarantined;
}
