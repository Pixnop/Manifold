using Manifold.Api;

namespace Manifold.Internal;

/// <summary>A single inventory category that must move from one owner profile to another.</summary>
/// <param name="Category">The inventory category.</param>
/// <param name="FromKey">Owner key the physical inventory currently belongs to.</param>
/// <param name="ToKey">Owner key it must become.</param>
internal readonly record struct InventorySwap(ManifoldInventory Category, string FromKey, string ToKey);
