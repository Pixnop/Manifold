using System;
using System.Collections.Generic;
using System.Linq;
using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class StreamingPlannerTests
{
    [Fact]
    public void Plan_Should_Return_Window_NearestFirst_Within_Budget()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, budgetPerTick: 100);

        Assert.Equal(9, planned.Count);
        Assert.Equal((10, 5, 5), (planned[0].DimId, planned[0].Cx, planned[0].Cz));
        var firstFive = planned.Take(5).Select(c => (c.Cx, c.Cz)).ToHashSet();
        Assert.Contains((5, 4), firstFive);
        Assert.Contains((4, 5), firstFive);
    }

    [Fact]
    public void Plan_Should_Respect_Budget()
    {
        var players = new[] { Player("p", 10, 5, 5, 2) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, budgetPerTick: 4);
        Assert.Equal(4, planned.Count);
        Assert.Equal((5, 5), (planned[0].Cx, planned[0].Cz));
    }

    [Fact]
    public void Plan_Should_Exclude_Already_Loaded_Columns()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        bool IsLoaded(int dim, int cx, int cz) => cx == 5 && cz == 5;
        var planned = StreamingPlanner.Plan(players, IsLoaded, budgetPerTick: 100);

        Assert.Equal(8, planned.Count);
        Assert.DoesNotContain(planned, c => c.Cx == 5 && c.Cz == 5);
    }

    [Fact]
    public void Plan_Should_Clip_Negative_Coordinates()
    {
        var players = new[] { Player("p", 10, 0, 0, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, budgetPerTick: 100);

        Assert.All(planned, c => Assert.True(c.Cx >= 0 && c.Cz >= 0));
        Assert.Equal(4, planned.Count);
    }

    [Fact]
    public void Plan_Should_Merge_Overlapping_Player_Windows()
    {
        var players = new[] { Player("a", 10, 5, 5, 1), Player("b", 10, 6, 5, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, budgetPerTick: 100);

        var shared = planned.Single(c => c.Cx == 5 && c.Cz == 5);
        Assert.Equal(new[] { "a", "b" }, shared.PlayerUids.OrderBy(u => u).ToArray());
    }

    [Fact]
    public void Plan_Should_Return_Empty_When_No_Players()
    {
        var planned = StreamingPlanner.Plan(Array.Empty<StreamingPlayer>(), NoneLoaded, budgetPerTick: 100);
        Assert.Empty(planned);
    }

    [Fact]
    public void Plan_Should_Return_Empty_When_All_Loaded()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        var planned = StreamingPlanner.Plan(players, (_, _, _) => true, budgetPerTick: 100);
        Assert.Empty(planned);
    }

    private static bool NoneLoaded(int dim, int cx, int cz) => false;

    private static StreamingPlayer Player(string uid, int dim, int cx, int cz, int r) => new(uid, dim, cx, cz, r);
}
