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

    // Manifold bug: OnTransitPlayerEntered (ManifoldModSystem.cs:422-433) reads the entity's
    // position synchronously off PlayerEntered, which TransitService raises right after calling
    // PlayerTeleporter.Teleport (PlayerTeleporter.cs:22). That call is Entity.TeleportToDouble,
    // whose own doc says the actual move is "delayed until target chunk is loaded" - so the
    // packet's TargetX/Y/Z is captured before the delayed move applies and reports the player's
    // PRE-transit (source dimension) coordinates instead of the documented target position.
    // Observed: teleporting atlas_mspot from the overworld to flat (real landing 512,6,512, per
    // player.Position once LandedAt settles) sent a PlayerTransitedPacket with TargetX/Y/Z equal
    // to the player's overworld position instead. This scenario asserts the documented contract
    // (TargetX/Y/Z is the actual landing position) and is skipped until the packet is sent from
    // the settled position instead of synchronously with the event.
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
