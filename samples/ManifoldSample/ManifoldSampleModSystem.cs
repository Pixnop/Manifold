using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ManifoldSample;

/// <summary>
/// Sample consumer mod. Registers manifoldsample:void using <c>BasicVoidWorldgenStrategy</c>
/// and exposes a <c>/voiddim</c> chat command for transit.
/// </summary>
public sealed class ManifoldSampleModSystem : ModSystem
{
    /// <summary>Run after Manifold (0.05) so the facade is ready.</summary>
    public override double ExecuteOrder() => 0.5;

    /// <inheritdoc/>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockClass("ManifoldSampleVoidPortal", typeof(VoidPortalBlock));
    }

    /// <inheritdoc/>
    public override void StartServerSide(ICoreServerAPI sapi)
    {
        base.StartServerSide(sapi);

        var manifold = sapi.GetManifoldServer();
        if (!manifold.IsHealthy)
        {
            Mod.Logger.Warning("[ManifoldSample] Manifold is unhealthy; dimension features disabled.");
            return;
        }

        manifold.Registry
            .Define(new AssetLocation("manifoldsample", "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .RegisterStatic();

        new DimensionCommandBuilder()
            .Command("voiddim")
            .TargetDimension(new AssetLocation("manifoldsample", "void"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the Manifold sample void dimension.")
            .Register(sapi);

        Mod.Logger.Notification(
            "[ManifoldSample] Registered void dimension and /voiddim command.");
    }
}
