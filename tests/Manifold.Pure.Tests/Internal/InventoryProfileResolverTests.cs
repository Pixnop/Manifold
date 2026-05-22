using System.Linq;
using Manifold.Api;
using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class InventoryProfileResolverTests
{
    [Fact]
    public void OwnerKeyFor_Should_Be_DimCode_When_Separated_Else_Shared()
    {
        Assert.Equal("mymod:vault", InventoryProfileResolver.OwnerKeyFor(ManifoldInventory.Backpack, "mymod:vault", ManifoldInventory.Backpack));
        Assert.Equal(InventoryProfileResolver.SharedKey, InventoryProfileResolver.OwnerKeyFor(ManifoldInventory.Backpack, "mymod:vault", ManifoldInventory.Hotbar));
    }

    [Fact]
    public void Plan_Should_Be_Empty_When_Shared_To_Shared()
    {
        var plan = InventoryProfileResolver.Plan(ManifoldInventory.None, "mymod:plain", _ => InventoryProfileResolver.SharedKey);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_Should_Swap_Separated_Categories_From_Shared()
    {
        var plan = InventoryProfileResolver.Plan(ManifoldInventory.Hotbar | ManifoldInventory.Backpack, "mymod:vault", _ => InventoryProfileResolver.SharedKey);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, s => Assert.Equal(InventoryProfileResolver.SharedKey, s.FromKey));
        Assert.All(plan, s => Assert.Equal("mymod:vault", s.ToKey));
        var cats = plan.Select(s => s.Category).ToHashSet();
        Assert.Contains(ManifoldInventory.Hotbar, cats);
        Assert.Contains(ManifoldInventory.Backpack, cats);
        Assert.DoesNotContain(ManifoldInventory.Character, cats);
    }

    [Fact]
    public void Plan_Should_Swap_From_Current_Dim_To_Another_Dim()
    {
        string CurrentKey(ManifoldInventory c) => c == ManifoldInventory.Backpack ? "mod:a" : InventoryProfileResolver.SharedKey;
        var plan = InventoryProfileResolver.Plan(ManifoldInventory.Backpack, "mod:b", CurrentKey);

        var swap = Assert.Single(plan);
        Assert.Equal(ManifoldInventory.Backpack, swap.Category);
        Assert.Equal("mod:a", swap.FromKey);
        Assert.Equal("mod:b", swap.ToKey);
    }

    [Fact]
    public void Plan_Should_Restore_To_Shared_When_Leaving_A_Separate_Dim()
    {
        string CurrentKey(ManifoldInventory c) => c == ManifoldInventory.Backpack ? "mod:vault" : InventoryProfileResolver.SharedKey;
        var plan = InventoryProfileResolver.Plan(ManifoldInventory.None, "mod:plain", CurrentKey);

        var swap = Assert.Single(plan);
        Assert.Equal("mod:vault", swap.FromKey);
        Assert.Equal(InventoryProfileResolver.SharedKey, swap.ToKey);
    }
}
