using Manifold.Api;
using Manifold.Internal;
using Manifold.Internal.Networking;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionDescriptorMapperTests
{
    [Fact]
    public void ToDescriptor_Should_Map_All_Fields()
    {
        var dim = new DimensionImpl(
            Code: new AssetLocation("mod:nether"),
            InternalId: 10,
            IsBuiltIn: false,
            Lifetime: DimensionLifetime.Persistent,
            OwnerModId: "mod",
            State: DimensionState.Active,
            Worldgen: null,
            GenerationRadius: DimensionBuilderImpl.DefaultGenerationRadius,
            SpawnBehavior: Manifold.Api.Transitions.SpawnBehavior.SameCoordinates,
            SpawnPoint: null,
            ForcedGameMode: null,
            StreamingLoadRadius: null,
            RelightHeight: DimensionBuilderImpl.DefaultRelightHeight,
            SeparateInventory: Manifold.Api.ManifoldInventory.None,
            Metadata: DimensionBuilderImpl.EmptyMetadata);

        var d = DimensionDescriptorMapper.ToDescriptor(dim);
        Assert.Equal("mod:nether", d.Code);
        Assert.Equal(10, d.InternalId);
        Assert.False(d.IsBuiltIn);
        Assert.Equal((int)DimensionLifetime.Persistent, d.Lifetime);
        Assert.Equal("mod", d.OwnerModId);
        Assert.Equal((int)DimensionState.Active, d.State);
    }

    [Fact]
    public void ToImpl_Should_Roundtrip_Through_Descriptor()
    {
        var original = new DimensionImpl(
            Code: new AssetLocation("mod:a"),
            InternalId: 42,
            IsBuiltIn: false,
            Lifetime: DimensionLifetime.Ephemeral,
            OwnerModId: "owner",
            State: DimensionState.Quarantined,
            Worldgen: null,
            GenerationRadius: DimensionBuilderImpl.DefaultGenerationRadius,
            SpawnBehavior: Manifold.Api.Transitions.SpawnBehavior.SameCoordinates,
            SpawnPoint: null,
            ForcedGameMode: null,
            StreamingLoadRadius: null,
            RelightHeight: DimensionBuilderImpl.DefaultRelightHeight,
            SeparateInventory: Manifold.Api.ManifoldInventory.None,
            Metadata: DimensionBuilderImpl.EmptyMetadata);

        var roundtrip = DimensionDescriptorMapper.ToImpl(DimensionDescriptorMapper.ToDescriptor(original));

        Assert.Equal(original.Code, roundtrip.Code);
        Assert.Equal(original.InternalId, roundtrip.InternalId);
        Assert.Equal(original.IsBuiltIn, roundtrip.IsBuiltIn);
        Assert.Equal(original.Lifetime, roundtrip.Lifetime);
        Assert.Equal(original.OwnerModId, roundtrip.OwnerModId);
        Assert.Equal(original.State, roundtrip.State);
        Assert.Null(roundtrip.Worldgen);
    }
}
