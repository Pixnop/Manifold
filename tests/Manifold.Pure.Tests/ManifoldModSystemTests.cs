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
    public void EvacuateOccupants_Should_Rescue_Every_Given_Player_Without_Re_Filtering()
    {
        // Callers now pass an already-filtered occupant list (e.g. OccupancyScan.PlayersIn), so
        // EvacuateOccupants itself must not re-check position before rescuing - it trusts the list.
        // A player whose recorded dimension does not match internalId still gets rescued here.
        var given = PlayerAt(7);
        bool rescued = false;

        var (evacuated, remaining) = ManifoldModSystem.EvacuateOccupants(
            new[] { given }, 42, _ => rescued = true);

        Assert.True(rescued);
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
