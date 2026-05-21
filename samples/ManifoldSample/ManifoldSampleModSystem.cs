using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ManifoldSample;

/// <summary>
/// Sample consumer mod. Registers two demo dimensions - manifoldsample:void (empty air, via
/// <c>BasicVoidWorldgenStrategy</c>) and manifoldsample:flat (solid floor, via
/// <c>FlatWorldgenStrategy</c>) - and exposes <c>/voiddim</c> and <c>/flatdim</c> chat commands.
/// </summary>
public sealed class ManifoldSampleModSystem : ModSystem
{
    private const string ModId = "manifoldsample";

    /// <summary>Run after Manifold (0.05) so the facade is ready.</summary>
    public override double ExecuteOrder() => 0.5;

    /// <inheritdoc/>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockClass("ManifoldSampleVoidPortal", typeof(VoidPortalBlock));
    }

    /// <inheritdoc/>
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        var manifold = api.GetManifoldServer(this);
        if (!manifold.IsHealthy)
        {
            Mod.Logger.Warning("[ManifoldSample] Manifold is unhealthy; dimension features disabled.");
            return;
        }

        manifold.Registry
            .Define(new AssetLocation(ModId, "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())

            // Spawn well away from the world corner (negative chunks are invalid in VS, so a
            // 0,0 spawn would be walled on two sides). Larger radius = more room to fly around.
            .WithFixedSpawn(new BlockPos(1024, 64, 1024, 0))
            .WithGenerationRadius(5)
            .RegisterStatic();

        new DimensionCommandBuilder()
            .Command("voiddim")
            .TargetDimension(new AssetLocation(ModId, "void"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the Manifold sample void dimension.")
            .Register(api);

        manifold.Registry
            .Define(new AssetLocation(ModId, "flat"))
            .Persistent()
            .WithWorldgen(new FlatWorldgenStrategy())
            .WithSpawnBehavior(SpawnBehavior.LastVisited)
            .WithGenerationRadius(4)
            .RegisterStatic();

        new DimensionCommandBuilder()
            .Command("flatdim")
            .TargetDimension(new AssetLocation(ModId, "flat"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the Manifold sample flat dimension (solid floor for movement testing).")
            .Register(api);

        manifold.Registry
            .Define(new AssetLocation(ModId, "stream"))
            .Persistent()
            .WithWorldgen(new FlatWorldgenStrategy())
            .Streaming(4)
            .RegisterStatic();

        new DimensionCommandBuilder()
            .Command("streamdim")
            .TargetDimension(new AssetLocation(ModId, "stream"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the streaming flat dimension (walk to watch chunks generate).")
            .Register(api);

        // The overworld is a first-class Manifold dimension (manifold:overworld, id 0).
        // This command demonstrates a clean round-trip back to it via the transit API.
        new DimensionCommandBuilder()
            .Command("overworlddim")
            .TargetDimension(new AssetLocation("manifold", "overworld"))
            .RequiresPrivilege("chat")
            .WithSpawnBehavior(SpawnBehavior.LastVisited)
            .DescribedAs("Teleport back to the overworld (to your last position there) via Manifold's transit API.")
            .Register(api);

        Mod.Logger.Notification(
            "[ManifoldSample] Registered void + flat + stream dimensions and /voiddim, /flatdim, /streamdim, /overworlddim commands.");
    }
}
