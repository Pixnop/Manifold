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

        // Demonstrates the 0.4.0 transit events. These log lines make PlayerArriving (#37) and
        // EntityChangedDimension (#41) observable in the server console during testing.
        manifold.Transitions.PlayerArriving += (_, e) =>
        {
            Mod.Logger.Notification(
                "[ManifoldSample] PlayerArriving: {0} -> {1} @ {2}",
                e.Player.PlayerName,
                e.TargetDimension.Code,
                e.TargetPosition);
        };
        manifold.Transitions.EntityChangedDimension += (_, e) =>
        {
            Mod.Logger.Notification(
                "[ManifoldSample] EntityChangedDimension: {0} {1} -> {2} @ {3}",
                e.Entity.Code,
                e.PreviousDimension.Code,
                e.NewDimension.Code,
                e.NewPosition);
        };

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

        manifold.Registry
            .Define(new AssetLocation(ModId, "vault"))
            .Persistent()
            .WithWorldgen(new FlatWorldgenStrategy())
            .WithSeparateInventory(Manifold.Api.ManifoldInventory.All)
            .RegisterStatic();

        new DimensionCommandBuilder()
            .Command("vaultdim")
            .TargetDimension(new AssetLocation(ModId, "vault"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the vault dimension (separate inventory; your items wait in the overworld).")
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

        // Demonstrates TeleportEntity: spawns a stick item entity at the caller and sends it to
        // the sample flat dimension via ITransitionService.TeleportEntity.
        api.ChatCommands.Create("sendtestitem")
            .WithDescription("Spawn a stick item entity and send it to the sample flat dimension.")
            .RequiresPrivilege("chat")
            .RequiresPlayer()
            .HandleWith(cmdArgs =>
            {
                if (cmdArgs.Caller.Player is not IServerPlayer serverPlayer)
                {
                    return TextCommandResult.Error("Players only.");
                }

                var item = api.World.GetItem(new AssetLocation("game:stick"));
                if (item is null)
                {
                    return TextCommandResult.Error("Test item not found.");
                }

                var stack = new ItemStack(item);

                // SidedPos is a property in every 1.21.x and 1.22.x API; reading Entity.Pos directly
                // would emit ldfld (against the 1.21 shape) or callvirt get_Pos (against 1.22), which
                // mismatches one of the two at runtime. SidedPos is marked obsolete in 1.22 only.
#pragma warning disable CS0618 // Type or member is obsolete
                var spawned = api.World.SpawnItemEntity(stack, serverPlayer.Entity.SidedPos.XYZ);
#pragma warning restore CS0618
                if (spawned is null)
                {
                    return TextCommandResult.Error("Failed to spawn the test item entity.");
                }

                manifold.Transitions.TeleportEntity(spawned, new AssetLocation(ModId, "flat"));
                return TextCommandResult.Success("Sent a stick to the flat dimension; use /flatdim to find it near your X/Z.");
            });

        // Demonstrates TeleportBlock (#36): teleports the block the caller is looking at - with its
        // BlockEntity contents (e.g. a chest's inventory) - to the flat dimension. Place a chest,
        // put items in it, look at it, then run /sendtestblock and check the chest in /flatdim.
        api.ChatCommands.Create("sendtestblock")
            .WithDescription("Teleport the block you are looking at (with its contents) to the flat dimension.")
            .RequiresPrivilege("chat")
            .RequiresPlayer()
            .HandleWith(cmdArgs =>
            {
                if (cmdArgs.Caller.Player is not IServerPlayer serverPlayer)
                {
                    return TextCommandResult.Error("Players only.");
                }

                if (serverPlayer.CurrentBlockSelection?.Position is not { } src)
                {
                    return TextCommandResult.Error("Look at a block first, then run /sendtestblock.");
                }

#pragma warning disable CS0618 // SidedPos is obsolete in 1.22 only; used for 1.21/1.22 binary compat.
                var ppos = serverPlayer.Entity.SidedPos;
#pragma warning restore CS0618
                var targetLocal = new BlockPos((int)ppos.X, 64, (int)ppos.Z, 0);

                bool moved = manifold.Transitions.TeleportBlock(
                    src, new AssetLocation(ModId, "flat"), targetLocal);

                return moved
                    ? TextCommandResult.Success(
                        $"Teleported the block to flat dim at ({targetLocal.X}, 64, {targetLocal.Z}). " +
                        "Use /flatdim and go to that X/Z to verify it (and its contents) arrived; the source slot is now air.")
                    : TextCommandResult.Error("Nothing moved - the targeted block was air.");
            });

        Mod.Logger.Notification(
            "[ManifoldSample] Registered void + flat + stream + vault dimensions and /voiddim, /flatdim, /streamdim, /vaultdim, /overworlddim, /sendtestitem, /sendtestblock commands.");
    }
}
