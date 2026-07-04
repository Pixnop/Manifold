namespace Manifold.Scenarios;

using Atlas.XUnit;

/// <summary>
/// Shared helpers for reading the results the atlasfixture mod publishes through
/// SaveGame data. This is the only channel back from the fixture: ExecuteCommand
/// returns void and Manifold types cannot cross the assembly identity boundary.
/// </summary>
public abstract class ManifoldScenarioBase : AtlasScenarioBase
{
    protected async Task<int> DimensionId(string path)
    {
        await World.Until(() => ReadDimensionId(path) is not null, timeoutTicks: 200);
        return ReadDimensionId(path)!.Value;
    }

    protected int? ReadDimensionId(string path)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("atlasfixture:dimid:" + path);
        return data is null ? null : BitConverter.ToInt32(data, 0);
    }

    protected bool FlagIsSet(string key)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData(key);
        return data is { Length: > 0 } && data[0] == 1;
    }
}
