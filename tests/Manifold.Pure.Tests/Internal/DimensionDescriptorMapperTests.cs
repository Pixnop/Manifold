using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Manifold.Api;
using Manifold.Internal;
using Manifold.Internal.Networking;
using ProtoBuf;
using Vintagestory.API.Common;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

public sealed class DimensionDescriptorMapperTests
{
    private enum SampleColor
    {
        Red = 0,
        Green = 5,
        Blue = 9,
    }

    [Flags]
    private enum SampleFlags : ulong
    {
        None = 0,
        HighBit = 1UL << 63,
    }

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
            SeparateInventory: Manifold.Api.ManifoldInventory.None,
            Metadata: DimensionBuilderImpl.EmptyMetadata,
            StreamingBudgetPerTick: null,
            SkyCapY: null);

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
            SeparateInventory: Manifold.Api.ManifoldInventory.None,
            Metadata: DimensionBuilderImpl.EmptyMetadata,
            StreamingBudgetPerTick: null,
            SkyCapY: null);

        var roundtrip = DimensionDescriptorMapper.ToImpl(DimensionDescriptorMapper.ToDescriptor(original));

        Assert.Equal(original.Code, roundtrip.Code);
        Assert.Equal(original.InternalId, roundtrip.InternalId);
        Assert.Equal(original.IsBuiltIn, roundtrip.IsBuiltIn);
        Assert.Equal(original.Lifetime, roundtrip.Lifetime);
        Assert.Equal(original.OwnerModId, roundtrip.OwnerModId);
        Assert.Equal(original.State, roundtrip.State);
        Assert.Null(roundtrip.Worldgen);
    }

    [Fact]
    public void Metadata_Should_RoundTrip_Every_Supported_Type_Through_Real_Protobuf_Serialization()
    {
        var metadata = new Dictionary<string, object?>
        {
            ["str"] = "hello",
            ["bool"] = true,
            ["sbyte"] = (sbyte)-7,
            ["byte"] = (byte)7,
            ["short"] = (short)-100,
            ["ushort"] = (ushort)100,
            ["int"] = -42,
            ["uint"] = 999u,
            ["long"] = -123456789012L,
            ["ulong"] = 123456789012UL,
            ["char"] = 'x',
            ["float"] = 1.5f,
            ["double"] = 3.14,
            ["intptr"] = (nint)12345,
            ["uintptr"] = (nuint)12345,
            ["bytes"] = new byte[] { 1, 2, 3 },
            ["enum"] = SampleColor.Blue,
            ["null"] = null,
        };

        var wireOut = DimensionDescriptorMapper.ToDescriptor(MakeDimension(metadata));
        var wireIn = RoundTripThroughRealProtobuf(wireOut);
        var roundtrip = DimensionDescriptorMapper.ToImpl(wireIn);

        Assert.Equal("hello", roundtrip.Metadata["str"]);
        Assert.Equal(true, roundtrip.Metadata["bool"]);
        Assert.Equal((sbyte)-7, roundtrip.Metadata["sbyte"]);
        Assert.Equal((byte)7, roundtrip.Metadata["byte"]);
        Assert.Equal((short)-100, roundtrip.Metadata["short"]);
        Assert.Equal((ushort)100, roundtrip.Metadata["ushort"]);
        Assert.Equal(-42, roundtrip.Metadata["int"]);
        Assert.Equal(999u, roundtrip.Metadata["uint"]);
        Assert.Equal(-123456789012L, roundtrip.Metadata["long"]);
        Assert.Equal(123456789012UL, roundtrip.Metadata["ulong"]);
        Assert.Equal('x', roundtrip.Metadata["char"]);
        Assert.Equal(1.5f, roundtrip.Metadata["float"]);
        Assert.Equal(3.14, roundtrip.Metadata["double"]);
        Assert.Equal((nint)12345, roundtrip.Metadata["intptr"]);
        Assert.Equal((nuint)12345, roundtrip.Metadata["uintptr"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, roundtrip.Metadata["bytes"]);
        Assert.Equal(SampleColor.Blue, roundtrip.Metadata["enum"]);
        Assert.Null(roundtrip.Metadata["null"]);
    }

    [Fact]
    public void Metadata_Should_Resolve_The_Actual_Enum_Type_Not_Just_Its_Underlying_Value()
    {
        var metadata = new Dictionary<string, object?> { ["color"] = SampleColor.Green };

        var wireOut = DimensionDescriptorMapper.ToDescriptor(MakeDimension(metadata));
        var wireIn = RoundTripThroughRealProtobuf(wireOut);
        var roundtrip = DimensionDescriptorMapper.ToImpl(wireIn);

        // Not just "5" (the underlying value): the real enum instance, so a consumer's
        // GetMetadata<SampleColor>("color") also works on the client mirror.
        var value = Assert.IsType<SampleColor>(roundtrip.Metadata["color"]);
        Assert.Equal(SampleColor.Green, value);
    }

    [Fact]
    public void Metadata_Should_RoundTrip_A_UInt64_Flags_Enum_Value_Above_LongMaxValue()
    {
        var metadata = new Dictionary<string, object?> { ["flags"] = SampleFlags.HighBit };

        var wireOut = DimensionDescriptorMapper.ToDescriptor(MakeDimension(metadata));
        var wireIn = RoundTripThroughRealProtobuf(wireOut);
        var roundtrip = DimensionDescriptorMapper.ToImpl(wireIn);

        // Convert.ToInt64 alone throws OverflowException for this value; the mapper must convert
        // through ulong so bit 63 survives the round trip.
        var value = Assert.IsType<SampleFlags>(roundtrip.Metadata["flags"]);
        Assert.Equal(SampleFlags.HighBit, value);
    }

    [Fact]
    public void Metadata_Should_ExposeRawUnderlyingValue_When_EnumTypeCannotBeResolved()
    {
        var descriptor = new DimensionDescriptor { Code = "mod:a" };
        descriptor.Metadata.Add(new MetadataEntry
        {
            Key = "unknown-enum",
            Kind = MetadataValueKind.Enum,
            IntegerValue = 7,
            EnumTypeFullName = "Nope.DoesNotExist",
            EnumAssemblyName = "NoSuchAssemblyAnywhere",
        });

        var impl = DimensionDescriptorMapper.ToImpl(descriptor);

        Assert.Equal(7L, impl.Metadata["unknown-enum"]);
    }

    [Fact]
    public void ToImpl_Should_Return_Empty_Metadata_When_Descriptor_Carries_None()
    {
        // The shape an old server (predating replicated metadata) sends: the field is simply absent.
        var descriptor = new DimensionDescriptor { Code = "mod:a" };

        var impl = DimensionDescriptorMapper.ToImpl(descriptor);

        Assert.Empty(impl.Metadata);
    }

    [Fact]
    public void ToDescriptor_Should_Produce_An_Empty_Metadata_List_For_A_Dimension_With_None()
    {
        var dim = MakeDimension(DimensionBuilderImpl.EmptyMetadata);

        var descriptor = DimensionDescriptorMapper.ToDescriptor(dim);

        Assert.Empty(descriptor.Metadata);
    }

    private static DimensionDescriptor RoundTripThroughRealProtobuf(DimensionDescriptor descriptor)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, descriptor);
        stream.Position = 0;
        return Serializer.Deserialize<DimensionDescriptor>(stream);
    }

    private static DimensionImpl MakeDimension(IReadOnlyDictionary<string, object?> metadata) => new(
        Code: new AssetLocation("mod:a"),
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
        SeparateInventory: Manifold.Api.ManifoldInventory.None,
        Metadata: metadata.ToImmutableDictionary(),
        StreamingBudgetPerTick: null,
        SkyCapY: null);
}
