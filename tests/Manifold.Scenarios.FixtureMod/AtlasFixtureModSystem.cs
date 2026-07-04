namespace AtlasFixture;

using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

/// <summary>
/// Server-side fixture driven by the Manifold.Scenarios suite. All Manifold API
/// calls live here because scenario code cannot share assembly identity with the
/// ModLoader-loaded Manifold.dll. Results are published through SaveGame data.
/// </summary>
public sealed class AtlasFixtureModSystem : ModSystem
{
    internal const string Domain = "atlasfixture";

    private ICoreServerAPI _sapi = null!;
    private IManifoldServer _manifold = null!;

    // Run after Manifold (0.05), like any consumer mod.
    public override double ExecuteOrder() => 0.5;

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        _sapi = api;
        _manifold = api.GetManifoldServer(this);

        IDimension flat = _manifold.Registry
            .Define(new AssetLocation(Domain, "flat"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(new BlockPos(512, 8, 512, 0))
            .WithGenerationRadius(2)
            .RegisterStatic();
        PublishDimensionId("flat", flat.InternalId);
        PregenerateSpawn(flat);

        IDimension voidDim = _manifold.Registry
            .Define(new AssetLocation(Domain, "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithFixedSpawn(new BlockPos(512, 8, 512, 0))
            .WithGenerationRadius(2)
            .RegisterStatic();
        PublishDimensionId("void", voidDim.InternalId);
        PregenerateSpawn(voidDim);
    }

    private void PublishDimensionId(string path, int internalId)
    {
        _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:dimid:{path}",
            BitConverter.GetBytes(internalId));
    }

    /// <summary>
    /// RegisterStatic only records the dimension; it does not generate any terrain. Manifold's
    /// active worldgen driver only runs through Transitions (player transit / join), so nothing
    /// generates a boot-registered dimension's spawn region on its own. Force it here via a no-op
    /// TeleportBlock: an air source block moved onto the fixed spawn column triggers
    /// DimensionGenerator.EnsureRegion for that column before any scenario can observe it.
    /// </summary>
    private void PregenerateSpawn(IDimension dimension)
    {
        var overworldAir = new BlockPos(0, 1, 0, 0);
        var target = new BlockPos(512, 8, 512, dimension.InternalId);
        _manifold.Transitions.TeleportBlock(overworldAir, dimension.Code, target);
    }
}
