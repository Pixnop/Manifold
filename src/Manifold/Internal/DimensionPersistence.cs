using System;
using System.Collections.Generic;
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

    private readonly IManifestStore _store;
    private readonly IModLoaderQuery _modLoaderQuery;

    /// <summary>Initializes a new instance of the <see cref="DimensionPersistence"/> class.</summary>
    /// <param name="store">Manifest byte store.</param>
    /// <param name="modLoaderQuery">Mod loader probe for orphan detection.</param>
    public DimensionPersistence(IManifestStore store, IModLoaderQuery modLoaderQuery)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _modLoaderQuery = modLoaderQuery ?? throw new ArgumentNullException(nameof(modLoaderQuery));
    }

    /// <summary>Persist the supplied manifest entries (skipping Ephemeral and BuiltIn).</summary>
    /// <param name="entries">Entries to persist.</param>
    public void Save(IEnumerable<ManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
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

    /// <summary>Load all manifest entries, or an empty sequence on corruption / missing data.</summary>
    /// <returns>Manifest entries (zero or more).</returns>
    public IEnumerable<ManifestEntry> LoadOrEmpty()
    {
        var raw = _store.Read(ManifestKey);
        if (raw is null || raw.Length == 0)
        {
            yield break;
        }

        TreeAttribute tree;
        try
        {
            tree = new TreeAttribute();
            tree.FromBytes(raw);
        }
        catch
        {
            // Corrupted manifest — drop silently; consumers will re-register at boot.
            yield break;
        }

        var list = tree.GetTreeAttribute("entries");
        if (list is null)
        {
            yield break;
        }

        foreach (var kvp in list)
        {
            var attr = kvp.Value;
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
        _modLoaderQuery.IsModLoaded(entry.OwnerModId)
            ? DimensionState.Pending
            : DimensionState.Quarantined;
}
