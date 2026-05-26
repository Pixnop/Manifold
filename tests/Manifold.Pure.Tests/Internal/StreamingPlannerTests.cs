using System;
using System.Collections.Generic;
using System.Linq;
using Manifold.Internal;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class StreamingPlannerTests
{
    private static readonly string[] PlayersAB = { "a", "b" };

    [Fact]
    public void Plan_Should_Return_Window_NearestFirst_Within_Budget()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 100);

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
        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 4);
        Assert.Equal(4, planned.Count);
        Assert.Equal((5, 5), (planned[0].Cx, planned[0].Cz));
    }

    [Fact]
    public void Plan_Should_Exclude_Already_Loaded_Columns()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        bool IsLoaded(int dim, int cx, int cz) => cx == 5 && cz == 5;
        var planned = StreamingPlanner.Plan(players, IsLoaded, _ => 100);

        Assert.Equal(8, planned.Count);
        Assert.DoesNotContain(planned, c => c.Cx == 5 && c.Cz == 5);
    }

    [Fact]
    public void Plan_Should_Clip_Negative_Coordinates()
    {
        var players = new[] { Player("p", 10, 0, 0, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 100);

        Assert.All(planned, c => Assert.True(c.Cx >= 0 && c.Cz >= 0));
        Assert.Equal(4, planned.Count);
    }

    [Fact]
    public void Plan_Should_Merge_Overlapping_Player_Windows()
    {
        var players = new[] { Player("a", 10, 5, 5, 1), Player("b", 10, 6, 5, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 100);

        var shared = planned.Single(c => c.Cx == 5 && c.Cz == 5);
        Assert.Equal(PlayersAB, shared.PlayerUids.OrderBy(u => u).ToArray());
    }

    [Fact]
    public void Plan_Should_Return_Empty_When_No_Players()
    {
        var planned = StreamingPlanner.Plan(Array.Empty<StreamingPlayer>(), NoneLoaded, _ => 100);
        Assert.Empty(planned);
    }

    [Fact]
    public void Plan_Should_Return_Empty_When_All_Loaded()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        var planned = StreamingPlanner.Plan(players, (_, _, _) => true, _ => 100);
        Assert.Empty(planned);
    }

    [Fact]
    public void Plan_Should_Apply_Per_Dimension_Budget()
    {
        // Two dims, two players. dim 10 budget = 2, dim 11 budget = 1.
        // Each player has a radius=2 window (25 candidate cols each).
        var players = new[]
        {
            Player("a", 10, 5, 5, 2),
            Player("b", 11, 5, 5, 2),
        };

        var planned = StreamingPlanner.Plan(players, NoneLoaded, dim => dim == 10 ? 2 : 1);

        Assert.Equal(2, planned.Count(c => c.DimId == 10));
        Assert.Equal(1, planned.Count(c => c.DimId == 11));
    }

    [Fact]
    public void Plan_Should_Not_Let_One_Dim_Starve_Another()
    {
        // dim 10 has a lot of demand (one player with radius 2 = 25 cols).
        // dim 11 has just one player. With per-dim budgets they each get their share.
        var players = new[]
        {
            Player("busy", 10, 5, 5, 2),
            Player("quiet", 11, 0, 0, 1),
        };

        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 4);

        // dim 11 sees its share (capped at 4 but only has 4 valid cols after negative-clip).
        Assert.True(planned.Any(c => c.DimId == 11), "dim 11 must not be starved by dim 10");
        Assert.Equal(4, planned.Count(c => c.DimId == 10));
    }

    [Fact]
    public void Plan_Should_Skip_Dim_With_Zero_Budget()
    {
        var players = new[] { Player("p", 10, 5, 5, 1) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 0);
        Assert.Empty(planned);
    }

    private static bool NoneLoaded(int dim, int cx, int cz) => false;

    private static StreamingPlayer Player(string uid, int dim, int cx, int cz, int r) => new(uid, dim, cx, cz, r);
}
