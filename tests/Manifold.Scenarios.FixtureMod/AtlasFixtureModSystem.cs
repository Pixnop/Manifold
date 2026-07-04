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

    private static readonly BlockPos FixedSpawn = new(512, 8, 512, 0);

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
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .RegisterStatic();
        PublishDimensionId("flat", flat.InternalId);
        PregenerateSpawn(flat);

        IDimension voidDim = _manifold.Registry
            .Define(new AssetLocation(Domain, "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithFixedSpawn(FixedSpawn)
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
    /// TeleportBlock: this call exists purely for its generate-destination-region side effect via
    /// DimensionGenerator.EnsureRegion, not for the move itself. The source position must be air
    /// so the move is a guaranteed no-op; a near-ceiling position at the world origin is reliably
    /// air, unlike y=1 near bedrock, and using a non-air source would actually move a real
    /// overworld block.
    /// </summary>
    private void PregenerateSpawn(IDimension dimension)
    {
        var overworldAir = new BlockPos(0, _sapi.WorldManager.MapSizeY - 2, 0, 0);
        var target = new BlockPos(FixedSpawn.X, FixedSpawn.Y, FixedSpawn.Z, dimension.InternalId);
        try
        {
            _manifold.Transitions.TeleportBlock(overworldAir, dimension.Code, target);
        }
        catch (Exception ex)
        {
            Mod.Logger.Error(
                "Spawn pregeneration failed for dimension {0}: {1}. Scenarios probing this dimension's terrain will time out.",
                dimension.Code,
                ex);
        }
    }
}
