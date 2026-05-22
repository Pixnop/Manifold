using System;

namespace Manifold.Api;

/// <summary>Player inventory categories a dimension can keep separate (see WithSeparateInventory).</summary>
[Flags]
public enum ManifoldInventory
{
    /// <summary>Nothing separated; the dimension shares the player's normal inventory (default).</summary>
    None = 0,

    /// <summary>The hotbar (quick slots).</summary>
    Hotbar = 1,

    /// <summary>The backpack (main inventory).</summary>
    Backpack = 2,

    /// <summary>Worn equipment / character slots.</summary>
    Character = 4,

    /// <summary>Hotbar, backpack and character.</summary>
    All = Hotbar | Backpack | Character,
}
