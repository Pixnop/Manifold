using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class MultiPositionBlockDetectorTests
{
    [Fact]
    public void IsMultiPosition_Should_Return_False_For_An_Ordinary_Block()
    {
        var accessor = Substitute.For<IBlockAccessor>();
        var pos = new BlockPos(10, 60, 10, 0);
        accessor.GetBlock(Arg.Any<BlockPos>()).Returns(new FakeOrdinaryBlock { Code = new AssetLocation("game:rock") });

        bool result = MultiPositionBlockDetector.IsMultiPosition(accessor, pos, out var reason);

        Assert.False(result);
        Assert.Null(reason);
    }

    [Fact]
    public void IsMultiPosition_Should_Detect_A_Satellite_Implementing_IMultiblockOffset()
    {
        var accessor = Substitute.For<IBlockAccessor>();
        var pos = new BlockPos(10, 60, 10, 0);
        var controller = new BlockPos(10, 59, 10, 0);
        var satellite = new FakeSatelliteBlock(controller) { Code = new AssetLocation("game:multiblock-monolithic-0-p1-0") };
        accessor.GetBlock(pos).Returns(satellite);

        bool result = MultiPositionBlockDetector.IsMultiPosition(accessor, pos, out var reason);

        Assert.True(result);
        Assert.Contains("satellite", reason);
        Assert.Contains(controller.ToString(), reason);
    }

    [Fact]
    public void IsMultiPosition_Should_Detect_A_Bed_Half_By_Type_Name()
    {
        var accessor = Substitute.For<IBlockAccessor>();
        var pos = new BlockPos(10, 60, 10, 0);
        accessor.GetBlock(pos).Returns(new Vintagestory.GameContent.BlockBed { Code = new AssetLocation("game:bed-wood-head-north") });

        bool result = MultiPositionBlockDetector.IsMultiPosition(accessor, pos, out var reason);

        Assert.True(result);
        Assert.Contains("bed", reason);
    }

    [Fact]
    public void IsMultiPosition_Should_Detect_A_Controller_Via_A_Neighbouring_Satellite()
    {
        var accessor = Substitute.For<IBlockAccessor>();
        var controllerPos = new BlockPos(10, 60, 10, 0);
        var satellitePos = new BlockPos(10, 61, 10, 0); // directly above: dy = 1, within radius.
        accessor.GetBlock(controllerPos).Returns(new FakeOrdinaryBlock { Code = new AssetLocation("game:door-solid-oak") });
        accessor.GetBlock(Arg.Is<BlockPos>(p => Same(p, satellitePos)))
            .Returns(new FakeSatelliteBlock(controllerPos) { Code = new AssetLocation("game:multiblock-monolithic-0-p1-0") });

        bool result = MultiPositionBlockDetector.IsMultiPosition(accessor, controllerPos, out var reason);

        Assert.True(result);
        Assert.Contains("controls a multiblock structure", reason);
    }

    [Fact]
    public void IsMultiPosition_Should_Ignore_A_Satellite_That_Points_At_A_Different_Controller()
    {
        var accessor = Substitute.For<IBlockAccessor>();
        var pos = new BlockPos(10, 60, 10, 0);
        var unrelatedSatellitePos = new BlockPos(10, 61, 10, 0);
        var unrelatedController = new BlockPos(50, 60, 50, 0); // some other structure entirely
        accessor.GetBlock(pos).Returns(new FakeOrdinaryBlock { Code = new AssetLocation("game:rock") });
        accessor.GetBlock(Arg.Is<BlockPos>(p => Same(p, unrelatedSatellitePos)))
            .Returns(new FakeSatelliteBlock(unrelatedController) { Code = new AssetLocation("game:multiblock-monolithic-0-p1-0") });

        bool result = MultiPositionBlockDetector.IsMultiPosition(accessor, pos, out var reason);

        Assert.False(result);
        Assert.Null(reason);
    }

    [Fact]
    public void IsMultiPosition_Should_Not_Search_Beyond_The_Documented_Vanilla_Offset_Range()
    {
        // Known ceiling: a modded satellite offset outside the vanilla multiblock's declared range
        // (dx/dz n2..p2, dy n2..p3) is invisible to the controller-side search. dy = 4 is one past
        // the documented p3 maximum.
        var accessor = Substitute.For<IBlockAccessor>();
        var controllerPos = new BlockPos(10, 60, 10, 0);
        var tooFarSatellitePos = new BlockPos(10, 64, 10, 0); // dy = 4
        accessor.GetBlock(controllerPos).Returns(new FakeOrdinaryBlock { Code = new AssetLocation("game:door-2x4gate-oak") });
        accessor.GetBlock(Arg.Is<BlockPos>(p => Same(p, tooFarSatellitePos)))
            .Returns(new FakeSatelliteBlock(controllerPos) { Code = new AssetLocation("game:multiblock-monolithic-0-p4-0") });

        bool result = MultiPositionBlockDetector.IsMultiPosition(accessor, controllerPos, out var reason);

        Assert.False(result);
        Assert.Null(reason);
    }

    private static bool Same(BlockPos a, BlockPos b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
}
