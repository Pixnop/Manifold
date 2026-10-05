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

    [Theory]
    [InlineData(2, 128, 12, 5)] // 128 blocks are 4 chunks, plus the one chunk of lead
    [InlineData(2, 129, 12, 6)] // a partial chunk rounds up, like the engine's send ring
    [InlineData(2, 32, 12, 2)] // 1 chunk of view plus the lead is 2: the loadRadius floor
    [InlineData(5, 32, 12, 5)] // the configured loadRadius is a floor
    [InlineData(2, 352, 12, 12)] // 11 chunks plus the lead reaches the server radius
    [InlineData(2, 384, 12, 12)] // 12 chunks: capped by MaxChunkRadius, the lead never goes past it
    [InlineData(2, 1000, 12, 12)] // far above the server radius: still capped
    [InlineData(2, 0, 12, 12)] // unknown view distance: the server radius, as before
    [InlineData(2, -1, 12, 12)]
    [InlineData(20, 128, 12, 20)] // a loadRadius above everything: still the floor
    [InlineData(1, 64, 0, 1)] // degenerate server radius: the floor
    public void WindowRadius_Should_Follow_The_Radius_The_Engine_Sends(int loadRadius, int viewDistance, int maxRadius, int expected)
    {
        Assert.Equal(expected, StreamingPlanner.WindowRadius(loadRadius, viewDistance, maxRadius));
    }

    [Fact]
    public void WindowRadius_Should_Grow_And_Shrink_With_The_View_Distance()
    {
        var grown = StreamingPlanner.WindowRadius(2, 256, 12);
        var shrunk = StreamingPlanner.WindowRadius(2, 64, 12);

        Assert.Equal(9, grown);
        Assert.Equal(3, shrunk);
        Assert.True(grown > shrunk);
    }

    [Fact]
    public void Plan_Should_Size_Each_Players_Window_On_Its_Own_Radius()
    {
        // Two players in one dimension, far apart, with different windows: the near-sighted one gets
        // 9 columns, the far-sighted one 25. Nothing from one window leaks into the other.
        var players = new[] { Player("near", 10, 10, 10, 1), Player("far", 10, 50, 50, 2) };
        var planned = StreamingPlanner.Plan(players, NoneLoaded, _ => 100);

        Assert.Equal(9, planned.Count(c => c.PlayerUids.Contains("near")));
        Assert.Equal(25, planned.Count(c => c.PlayerUids.Contains("far")));
        Assert.Equal(34, planned.Count);
    }

    [Fact]
    public void Plan_Should_Stop_Asking_For_Outer_Columns_When_A_Window_Shrinks()
    {
        var wide = StreamingPlanner.Plan(new[] { Player("p", 10, 20, 20, 3) }, NoneLoaded, _ => 100);
        var narrow = StreamingPlanner.Plan(new[] { Player("p", 10, 20, 20, 1) }, NoneLoaded, _ => 100);

        Assert.Equal(49, wide.Count);
        Assert.Equal(9, narrow.Count);
        Assert.All(narrow, c => Assert.Contains((c.Cx, c.Cz), wide.Select(w => (w.Cx, w.Cz))));
    }

    private static bool NoneLoaded(int dim, int cx, int cz) => false;

    private static StreamingPlayer Player(string uid, int dim, int cx, int cz, int r) => new(uid, dim, cx, cz, r);
}
