using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests;

/// <summary>
/// Tests for <see cref="ManifoldModSystem"/>'s pure helpers.
/// </summary>
public sealed class ManifoldModSystemTests
{
    [Fact]
    public void EvacuateOccupants_Should_Count_Only_Players_Rescue_Actually_Moved()
    {
        const int dimId = 42;
        var stuck = PlayerAt(dimId);
        var moved = PlayerAt(dimId);

        // "rescue" is best-effort: it succeeds for one occupant and silently fails for the other
        // (e.g. TeleportPlayer threw and RescueToOverworld swallowed it), same as production.
        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { stuck, moved },
            dimId,
            p =>
            {
                if (p == moved)
                {
                    p.Entity.Pos.Dimension = 0;
                }
            });

        Assert.Equal(1, evacuated);
        Assert.Equal(1, remaining);
    }

    [Fact]
    public void EvacuateOccupants_Should_Ignore_Players_In_Other_Dimensions()
    {
        var elsewhere = PlayerAt(7);
        bool rescued = false;

        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { elsewhere }, 42, _ => rescued = true);

        Assert.False(rescued);
        Assert.Equal(0, evacuated);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void EvacuateOccupants_Should_Report_Remaining_When_No_One_Is_Evacuated()
    {
        const int dimId = 42;
        var stuck = PlayerAt(dimId);

        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { stuck }, dimId, _ => { /* best-effort rescue that does nothing */ });

        Assert.Equal(0, evacuated);
        Assert.Equal(1, remaining);
    }

    // Entity.Pos is not a virtual member, so it cannot be stubbed; the substitute carries a real
    // EntityPos (created by the entity constructor) whose Dimension we set directly. EntityPosAccess
    // reads that same instance via reflection (see ManifoldServerFacadeTests.PlayerAt).
    private static IServerPlayer PlayerAt(int dimension)
    {
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = dimension;
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(entity);
        return player;
    }
}
