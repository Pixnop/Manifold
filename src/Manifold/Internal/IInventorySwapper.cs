using Manifold.Api;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Reads, clears and restores a player's inventory categories on the server. Abstracted so the
/// transit service can be tested without a live Vintage Story inventory and so a failure can be
/// simulated.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal interface IInventorySwapper
{
    /// <summary>Serialises a category's current contents to bytes (empty array if the inventory is missing).</summary>
    /// <param name="player">The player.</param>
    /// <param name="category">The inventory category.</param>
    /// <returns>Serialised inventory bytes.</returns>
    byte[] Serialize(IServerPlayer player, ManifoldInventory category);

    /// <summary>Restores a category's contents from bytes produced by <see cref="Serialize"/>.</summary>
    /// <param name="player">The player.</param>
    /// <param name="category">The inventory category.</param>
    /// <param name="bytes">Bytes from a previous <see cref="Serialize"/>.</param>
    void Restore(IServerPlayer player, ManifoldInventory category, byte[] bytes);

    /// <summary>Empties a category's inventory.</summary>
    /// <param name="player">The player.</param>
    /// <param name="category">The inventory category.</param>
    void Clear(IServerPlayer player, ManifoldInventory category);
}
