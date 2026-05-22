using System;
using System.Collections.Generic;
using Manifold.Api;

namespace Manifold.Internal;

/// <summary>
/// Pure planner for per-dimension inventories. Decides which categories must swap on a transit and
/// to which owner key. No engine dependency.
/// </summary>
internal static class InventoryProfileResolver
{
    /// <summary>Owner key used by every dimension that does not separate a category.</summary>
    public const string SharedKey = "shared";

    private static readonly ManifoldInventory[] Categories =
    {
        ManifoldInventory.Hotbar,
        ManifoldInventory.Backpack,
        ManifoldInventory.Character,
    };

    /// <summary>Returns the owner key a dimension uses for a category: its code if separated, else shared.</summary>
    /// <param name="separateFlags">Categories the dimension separates.</param>
    /// <param name="dimCode">The dimension code.</param>
    /// <param name="category">The category to query.</param>
    /// <returns>The dimension code when <paramref name="category"/> is in <paramref name="separateFlags"/>, otherwise <see cref="SharedKey"/>.</returns>
    public static string OwnerKeyFor(ManifoldInventory separateFlags, string dimCode, ManifoldInventory category) =>
        separateFlags.HasFlag(category) ? dimCode : SharedKey;

    /// <summary>
    /// Returns the categories that must swap when entering a dimension, given that dimension's
    /// separation flags and code and the player's current owner key per category.
    /// </summary>
    /// <param name="destFlags">Destination dimension's separated categories.</param>
    /// <param name="destCode">Destination dimension code.</param>
    /// <param name="currentKeyOf">Player's current owner key for a category (shared if unknown).</param>
    /// <returns>The swaps to apply (categories whose owner key changes).</returns>
    public static IReadOnlyList<InventorySwap> Plan(
        ManifoldInventory destFlags,
        string destCode,
        Func<ManifoldInventory, string> currentKeyOf)
    {
        ArgumentNullException.ThrowIfNull(currentKeyOf);

        var swaps = new List<InventorySwap>();
        foreach (var category in Categories)
        {
            string from = currentKeyOf(category);
            if (string.IsNullOrEmpty(from))
            {
                from = SharedKey;
            }

            string to = OwnerKeyFor(destFlags, destCode, category);
            if (!string.Equals(from, to, StringComparison.Ordinal))
            {
                swaps.Add(new InventorySwap(category, from, to));
            }
        }

        return swaps;
    }
}
