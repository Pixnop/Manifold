using System;
using Manifold.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Reads, clears and restores a player's inventory categories on the server. The only component that
/// touches Vintage Story inventory APIs; the swap policy lives in <see cref="InventoryProfileResolver"/>.
/// </summary>
/// <remarks>Server-side, main thread.</remarks>
internal sealed class InventorySwapper : IInventorySwapper
{
    private readonly ICoreServerAPI _sapi;

    /// <summary>Initializes a new instance of the <see cref="InventorySwapper"/> class.</summary>
    /// <param name="sapi">Server API.</param>
    public InventorySwapper(ICoreServerAPI sapi) =>
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));

    /// <inheritdoc/>
    public byte[] Serialize(IServerPlayer player, ManifoldInventory category)
    {
        var inv = GetInventory(player, category);
        if (inv is null)
        {
            return Array.Empty<byte>();
        }

        var tree = new TreeAttribute();
        inv.ToTreeAttributes(tree);
        return tree.ToBytes();
    }

    /// <inheritdoc/>
    public void Restore(IServerPlayer player, ManifoldInventory category, byte[] bytes)
    {
        var inv = GetInventory(player, category);
        if (inv is null || bytes is not { Length: > 0 })
        {
            Clear(player, category);
            return;
        }

        var tree = TreeAttribute.CreateFromBytes(bytes);
        inv.FromTreeAttributes(tree);
        inv.AfterBlocksLoaded(_sapi.World);
        player.BroadcastPlayerData(true);
    }

    /// <inheritdoc/>
    public void Clear(IServerPlayer player, ManifoldInventory category)
    {
        var inv = GetInventory(player, category);
        if (inv is null)
        {
            return;
        }

        for (int i = 0; i < inv.Count; i++)
        {
            if (inv[i]?.Itemstack is not null)
            {
                inv[i].Itemstack = null;
                inv[i].MarkDirty();
            }
        }

        player.BroadcastPlayerData(true);
    }

    private static InventoryBase? GetInventory(IServerPlayer player, ManifoldInventory category)
    {
        string className = category switch
        {
            ManifoldInventory.Hotbar => GlobalConstants.hotBarInvClassName,
            ManifoldInventory.Backpack => GlobalConstants.backpackInvClassName,
            ManifoldInventory.Character => GlobalConstants.characterInvClassName,
            _ => string.Empty,
        };

        return string.IsNullOrEmpty(className)
            ? null
            : player.InventoryManager.GetOwnInventory(className) as InventoryBase;
    }
}
