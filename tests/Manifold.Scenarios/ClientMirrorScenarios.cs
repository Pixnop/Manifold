namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Manifold.Internal.Networking;
using Xunit;

/// <summary>
/// The dimension mirror Manifold pushes to clients over its "manifold:dims" channel: a full
/// manifest on join, and incremental Added/Removed/Transited notifications afterward. The
/// packet types are internal to Manifold.dll, but InternalsVisibleTo grants this assembly access
/// to them as plain data (deserialized by full type name, same as any client-mirroring mod would).
/// </summary>
[Trait("Category", "E2E")]
public class ClientMirrorScenarios : ManifoldScenarioBase
{
    private const string Channel = "manifold:dims";

    [AtlasScenario]
    public async Task Manifest_Should_ListFixtureDimensionsAndOverworld_When_PlayerJoins()
    {
        int flatId = await DimensionId("flat");

        ITestPlayer player = await World.JoinPlayer("atlas_mjoin");
        await World.Ticks(2);

        ManifestSnapshotPacket snapshot = Assert.Single(player.Client.Packets<ManifestSnapshotPacket>(Channel));

        DimensionDescriptor flat = Assert.Single(snapshot.Dimensions, d => d.Code == "atlasfixture:flat");
        Assert.Equal(flatId, flat.InternalId);
        Assert.False(flat.IsBuiltIn);

        DimensionDescriptor overworld = Assert.Single(snapshot.Dimensions, d => d.Code == "manifold:overworld");
        Assert.Equal(0, overworld.InternalId);
        Assert.True(overworld.IsBuiltIn);
    }

    [AtlasScenario]
    public async Task Manifest_Should_IncludeDimensionMetadata_When_PlayerJoins()
    {
        await DimensionId("flat");

        ITestPlayer player = await World.JoinPlayer("atlas_mmeta");
        await World.Ticks(2);

        ManifestSnapshotPacket snapshot = Assert.Single(player.Client.Packets<ManifestSnapshotPacket>(Channel));
        DimensionDescriptor flat = Assert.Single(snapshot.Dimensions, d => d.Code == "atlasfixture:flat");

        AssertMetadata(flat, "fixture-label", MetadataValueKind.String, stringValue: "granite-slab");
        AssertMetadata(flat, "fixture-level", MetadataValueKind.Int32, integerValue: 3);
        AssertMetadata(flat, "fixture-active", MetadataValueKind.Boolean, integerValue: 1);
        AssertMetadata(flat, "fixture-signature", MetadataValueKind.ByteArray, bytesValue: new byte[] { 1, 2, 3 });
        AssertMetadata(flat, "fixture-note", MetadataValueKind.Null);

        MetadataEntry tint = Single(flat, "fixture-tint");
        Assert.Equal(MetadataValueKind.Enum, tint.Kind);
        Assert.Equal(7, tint.IntegerValue);
        Assert.Equal("AtlasFixture.FixtureTint", tint.EnumTypeFullName);
        Assert.Equal("AtlasFixture", tint.EnumAssemblyName);
    }

    [AtlasScenario]
    public async Task Client_Should_ReceiveDimensionMetadata_When_FixtureCreatesEphemeralDimensionWithMetadata()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_maddmeta");
        await World.Ticks(2);
        player.Client.Clear();

        await Ok("/atlasfx create-ephemeral mirroraddmeta");

        DimensionAddedPacket packet = Assert.Single(player.Client.Packets<DimensionAddedPacket>(Channel));
        MetadataEntry tint = Single(packet.Dimension, "fixture-tint");
        Assert.Equal(MetadataValueKind.Enum, tint.Kind);
        Assert.Equal(7, tint.IntegerValue);
        Assert.Equal("AtlasFixture.FixtureTint", tint.EnumTypeFullName);
    }

    [AtlasScenario]
    public async Task Client_Should_ReceiveDimensionAdded_When_FixtureCreatesEphemeralDimension()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_madd");
        await World.Ticks(2);
        player.Client.Clear();

        await Ok("/atlasfx create-ephemeral mirroradd");
        int newId = await DimensionId("mirroradd");

        DimensionAddedPacket packet = Assert.Single(player.Client.Packets<DimensionAddedPacket>(Channel));
        Assert.Equal("atlasfixture:mirroradd", packet.Dimension.Code);
        Assert.Equal(newId, packet.Dimension.InternalId);
    }

    [AtlasScenario]
    public async Task Client_Should_ReceiveDimensionRemoved_When_FixtureRemovesDimension()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_mrm");
        await World.Ticks(2);

        await Ok("/atlasfx create-ephemeral mirrorrm");
        int removedId = await DimensionId("mirrorrm");
        player.Client.Clear();

        await Ok("/atlasfx remove mirrorrm");

        DimensionRemovedPacket packet = Assert.Single(player.Client.Packets<DimensionRemovedPacket>(Channel));
        Assert.Equal("atlasfixture:mirrorrm", packet.Code);
        Assert.Equal(removedId, packet.InternalId);
    }

    [AtlasScenario]
    public async Task Player_Should_ReceivePlayerTransitedWithCorrectCodes_When_ChangingDimension()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_mtransit");
        await World.Ticks(2);
        player.Client.Clear();

        await Ok("/atlasfx teleport-player atlas_mtransit flat");

        PlayerTransitedPacket toFlatPacket = Assert.Single(player.Client.Packets<PlayerTransitedPacket>(Channel));
        Assert.Equal("manifold:overworld", toFlatPacket.SourceCode);
        Assert.Equal("atlasfixture:flat", toFlatPacket.TargetCode);

        player.Client.Clear();
        await Ok("/atlasfx teleport-player atlas_mtransit overworld");

        PlayerTransitedPacket toOverworldPacket = Assert.Single(player.Client.Packets<PlayerTransitedPacket>(Channel));
        Assert.Equal("atlasfixture:flat", toOverworldPacket.SourceCode);
        Assert.Equal("manifold:overworld", toOverworldPacket.TargetCode);
    }

    // Regression guard: the packet's TargetX/Y/Z must be the resolved landing position
    // (TransitService resolves it and passes it through PlayerEnteredDimensionEventArgs before
    // OnTransitPlayerEntered sends the packet), not the player's pre-transit, source-dimension
    // coordinates.
    [AtlasScenario]
    public async Task PlayerTransited_Should_ReportLandingPosition_When_PlayerArrives()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_mspot");
        await World.Ticks(2);
        player.Client.Clear();

        await Ok("/atlasfx teleport-player atlas_mspot flat");
        await LandedAt(player, flatId, 512, 512);

        PlayerTransitedPacket toFlatPacket = Assert.Single(player.Client.Packets<PlayerTransitedPacket>(Channel));
        Assert.Equal(512, toFlatPacket.TargetX);
        Assert.Equal(6, toFlatPacket.TargetY);
        Assert.Equal(512, toFlatPacket.TargetZ);
    }

    // Regression for the transit-out-of-ephemeral bug: the last occupant leaving an ephemeral
    // dimension both sends a PlayerTransitedPacket (for the player) and reaps the now-empty
    // dimension, which broadcasts a DimensionRemovedPacket for the very code the transit packet
    // names as its source. ManifoldModSystem sends the transit packet before reaping so a client
    // applying packets in arrival order still finds the source in its mirror when it resolves the
    // transit. IClientObservations exposes no cross-type packet sequencing, so this asserts both
    // packets' content rather than their wire order; the ordering itself is fixed at the call site
    // (OnTransitPlayerEntered sends before ReapEphemeralIfEmpty runs) and covered by
    // ClientTransitHandlerTests, which documents why the order matters to a resolving client.
    [AtlasScenario]
    public async Task Client_Should_ReceiveTransitedAndRemoved_When_LastOccupantLeavesEphemeral()
    {
        await Ok("/atlasfx create-ephemeral mirrorleave");
        int leaveId = await DimensionId("mirrorleave");
        ITestPlayer player = await World.JoinPlayer("atlas_mleave");
        await Ok("/atlasfx teleport-player atlas_mleave mirrorleave");
        await LandedAt(player, leaveId, 512, 512);
        player.Client.Clear();

        await Ok("/atlasfx teleport-player atlas_mleave overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        PlayerTransitedPacket transited = Assert.Single(player.Client.Packets<PlayerTransitedPacket>(Channel));
        Assert.Equal("atlasfixture:mirrorleave", transited.SourceCode);
        Assert.Equal("manifold:overworld", transited.TargetCode);

        DimensionRemovedPacket removed = Assert.Single(player.Client.Packets<DimensionRemovedPacket>(Channel));
        Assert.Equal("atlasfixture:mirrorleave", removed.Code);
        Assert.Equal(leaveId, removed.InternalId);
    }

    private static MetadataEntry Single(DimensionDescriptor descriptor, string key) =>
        Assert.Single(descriptor.Metadata, e => e.Key == key);

    private static void AssertMetadata(
        DimensionDescriptor descriptor,
        string key,
        MetadataValueKind kind,
        long integerValue = 0,
        string? stringValue = null,
        byte[]? bytesValue = null)
    {
        MetadataEntry entry = Single(descriptor, key);
        Assert.Equal(kind, entry.Kind);
        Assert.Equal(integerValue, entry.IntegerValue);
        Assert.Equal(stringValue, entry.StringValue);
        Assert.Equal(bytesValue, entry.BytesValue);
    }
}
