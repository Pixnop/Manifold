namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

[Trait("Category", "E2E")]
public class MetadataScenarios : ManifoldScenarioBase
{
    [AtlasTheory]
    [InlineData("level", "Int32:7")]
    [InlineData("name", "String:Atlas Meta")]
    [InlineData("mode", "EnumGameMode:Creative")]
    [InlineData("blob", "bytes:1-2-3")]
    [InlineData("empty", "null")]
    [InlineData("missing", "absent")]
    public async Task Metadata_Should_RoundTripTypedValues_When_DeclaredAtRegistration(string key, string expected)
    {
        await DimensionId("meta");
        CommandResult result = await World.ExecuteCommand($"/atlasfx metadata meta {key}");
        Assert.True(result.Ok, result.Message);
        Assert.Equal(expected, result.Message);
    }
}
