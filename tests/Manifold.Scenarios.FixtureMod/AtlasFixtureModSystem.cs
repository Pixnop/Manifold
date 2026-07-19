namespace AtlasFixture;

using System.Globalization;
using System.Linq;
using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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

        // Boot counter + first-boot dimension ids: the persistence scenarios compare state
        // across a real server restart (Atlas RestartWorld), so the first boot's values must
        // be readable after the second boot.
        byte[]? bootData = api.WorldManager.SaveGame.GetData($"{Domain}:bootcount");
        int bootCount = (bootData is null ? 0 : BitConverter.ToInt32(bootData, 0)) + 1;
        api.WorldManager.SaveGame.StoreData($"{Domain}:bootcount", BitConverter.GetBytes(bootCount));

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

        IDimension vault = _manifold.Registry
            .Define(new AssetLocation(Domain, "vault"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .WithSeparateInventory(ManifoldInventory.All)
            .RegisterStatic();
        PublishDimensionId("vault", vault.InternalId);
        PregenerateSpawn(vault);

        // Metadata-only dimension: no terrain probes, so no pregeneration needed.
        IDimension meta = _manifold.Registry
            .Define(new AssetLocation(Domain, "meta"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithMetadata("level", 7)
            .WithMetadata("name", "Atlas Meta")
            .WithMetadata("mode", EnumGameMode.Creative)
            .WithMetadata("blob", new byte[] { 1, 2, 3 })
            .WithMetadata("empty", null)
            .RegisterStatic();
        PublishDimensionId("meta", meta.InternalId);

        _manifold.Transitions.PlayerEntered += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-entered:{e.TargetDimension.Code.Path}", new[] { (byte)1 });
        _manifold.Transitions.PlayerLeft += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-left:{e.SourceDimension.Code.Path}", new[] { (byte)1 });
        _manifold.Transitions.PlayerArriving += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-arriving:{e.TargetDimension.Code.Path}", new[] { (byte)1 });
        _manifold.Registry.Destroyed += (_, e) =>
        {
            if (e.Dimension.Code.Domain == Domain)
            {
                _sapi.WorldManager.SaveGame.StoreData(
                    $"{Domain}:event:destroyed:{e.Dimension.Code.Path}", new[] { (byte)1 });
            }
        };

        RegisterCommands(api);
    }

    private void PublishDimensionId(string path, int internalId)
    {
        _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:dimid:{path}",
            BitConverter.GetBytes(internalId));

        // First-boot id, written once: lets a post-restart scenario verify id stability.
        string firstBootKey = $"{Domain}:dimid-firstboot:{path}";
        if (_sapi.WorldManager.SaveGame.GetData(firstBootKey) is null)
        {
            _sapi.WorldManager.SaveGame.StoreData(firstBootKey, BitConverter.GetBytes(internalId));
        }
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

    private void RegisterCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create("atlasfx")
            .WithDescription("Drives the Manifold API for Atlas integration scenarios.")
            .RequiresPrivilege("controlserver")
            .BeginSubCommand("teleport-entity")
                .WithArgs(parsers.Word("entityid"), parsers.Word("dimpath"))
                .HandleWith(OnTeleportEntity)
            .EndSubCommand()
            .BeginSubCommand("teleport-block")
                .WithArgs(
                    parsers.Int("x"),
                    parsers.Int("y"),
                    parsers.Int("z"),
                    parsers.Int("srcdim"),
                    parsers.Word("dimpath"),
                    parsers.Int("tx"),
                    parsers.Int("ty"),
                    parsers.Int("tz"))
                .HandleWith(OnTeleportBlock)
            .EndSubCommand()
            .BeginSubCommand("create-ephemeral")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnCreateEphemeral)
            .EndSubCommand()
            .BeginSubCommand("remove")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnRemove)
            .EndSubCommand()
            .BeginSubCommand("teleport-player")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"))
                .HandleWith(OnTeleportPlayer)
            .EndSubCommand()
            .BeginSubCommand("create-persistent")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnCreatePersistent)
            .EndSubCommand()
            .BeginSubCommand("create-darksky")
                .WithArgs(parsers.Word("dimpath"), parsers.Int("ceiling"))
                .HandleWith(OnCreateDarkSky)
            .EndSubCommand()
            .BeginSubCommand("force-remove")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnForceRemove)
            .EndSubCommand()
            .BeginSubCommand("kick")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnKick)
            .EndSubCommand()
            .BeginSubCommand("metadata")
                .WithArgs(parsers.Word("dimpath"), parsers.Word("key"))
                .HandleWith(OnMetadata)
            .EndSubCommand();
    }

    private TextCommandResult OnCreatePersistent(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension dimension = _manifold.Registry
            .Define(new AssetLocation(Domain, path))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1)
            .Create();
        PublishDimensionId(path, dimension.InternalId);
        PregenerateSpawn(dimension);
        return TextCommandResult.Success($"created {dimension.InternalId}");
    }

    private TextCommandResult OnCreateDarkSky(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        int ceiling = (int)args[1];
        IDimension dimension = _manifold.Registry
            .Define(new AssetLocation(Domain, path))
            .Ephemeral()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1)
            .WithDarkSky(ceiling)
            .Create();
        PublishDimensionId(path, dimension.InternalId);
        PregenerateSpawn(dimension);
        return TextCommandResult.Success($"created {dimension.InternalId}");
    }

    private TextCommandResult OnForceRemove(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        try
        {
            bool removed = _manifold.ForceRemoveDimension(new AssetLocation(Domain, path));
            return TextCommandResult.Success(removed ? "removed" : "not-removed");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error(ex.Message);
        }
    }

    private TextCommandResult OnKick(TextCommandCallingArgs args)
    {
        var playerName = (string)args[0];
        IServerPlayer? player = _sapi.World.AllOnlinePlayers
            .OfType<IServerPlayer>()
            .FirstOrDefault(p => p.PlayerName == playerName);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {playerName}.");
        }

        player.Disconnect("Kicked by the Atlas fixture.");
        return TextCommandResult.Success("kicked");
    }

    private TextCommandResult OnMetadata(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        var key = (string)args[1];

        IDimension? dimension = _manifold.Registry.Get(new AssetLocation(Domain, path));
        if (dimension is null)
        {
            return TextCommandResult.Error($"No dimension {path}.");
        }

        if (!dimension.HasMetadata(key))
        {
            return TextCommandResult.Success("absent");
        }

        object? value = dimension.Metadata[key];
        string rendered = value switch
        {
            null => "null",
            byte[] bytes => "bytes:" + string.Join('-', bytes),
            _ => value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        return TextCommandResult.Success(rendered);
    }

    private TextCommandResult OnTeleportEntity(TextCommandCallingArgs args)
    {
        long entityId = long.Parse((string)args[0], CultureInfo.InvariantCulture);
        var target = new AssetLocation(Domain, (string)args[1]);

        Entity? entity = _sapi.World.GetEntityById(entityId);
        if (entity is null)
        {
            return TextCommandResult.Error($"No entity with id {entityId}.");
        }

        // Land inside the pre-generated area around every fixture dimension's fixed spawn,
        // two blocks below the fixed spawn's Y (matches the granite slab's air pocket).
        var overridePosition = new BlockPos(FixedSpawn.X, FixedSpawn.Y - 2, FixedSpawn.Z, 0);
        var options = new TransitionOptions { OverridePosition = overridePosition };
        _manifold.Transitions.TeleportEntity(entity, target, options);
        return TextCommandResult.Success("ok");
    }

    private TextCommandResult OnTeleportPlayer(TextCommandCallingArgs args)
    {
        var playerName = (string)args[0];
        var dimPath = (string)args[1];

        IServerPlayer? player = _sapi.World.AllOnlinePlayers
            .OfType<IServerPlayer>()
            .FirstOrDefault(p => p.PlayerName == playerName);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {playerName}.");
        }

        if (dimPath == "overworld")
        {
            var target = new AssetLocation("manifold", "overworld");

            // AsBlockPos carries the EntityPos dimension through; the vanilla default spawn is dimension 0.
            var options = new TransitionOptions { OverridePosition = _sapi.World.DefaultSpawnPosition.AsBlockPos };
            _manifold.Transitions.TeleportPlayer(player, target, options);
        }
        else
        {
            var target = new AssetLocation(Domain, dimPath);
            var overridePosition = new BlockPos(FixedSpawn.X, FixedSpawn.Y - 2, FixedSpawn.Z, 0);
            var options = new TransitionOptions { OverridePosition = overridePosition };
            _manifold.Transitions.TeleportPlayer(player, target, options);
        }

        return TextCommandResult.Success("ok");
    }

    private TextCommandResult OnTeleportBlock(TextCommandCallingArgs args)
    {
        var source = new BlockPos((int)args[0], (int)args[1], (int)args[2], (int)args[3]);
        var target = new AssetLocation(Domain, (string)args[4]);
        var targetLocal = new BlockPos((int)args[5], (int)args[6], (int)args[7], 0);

        bool moved = _manifold.Transitions.TeleportBlock(source, target, targetLocal);
        return TextCommandResult.Success(moved ? "moved" : "no-op");
    }

    private TextCommandResult OnCreateEphemeral(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension dimension = _manifold.Registry
            .Define(new AssetLocation(Domain, path))
            .Ephemeral()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1)
            .Create();
        PublishDimensionId(path, dimension.InternalId);

        // Same pregeneration problem as boot-time dimensions: Create() only registers the
        // dimension, it does not generate terrain. Force it here too, or the scenario's
        // granite probe will time out.
        PregenerateSpawn(dimension);
        return TextCommandResult.Success($"created {dimension.InternalId}");
    }

    private TextCommandResult OnRemove(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        try
        {
            bool removed = _manifold.Registry.TryRemove(new AssetLocation(Domain, path));
            return TextCommandResult.Success(removed ? "removed" : "not-removed");
        }
        catch (ManifoldException ex)
        {
            // Persistent/BuiltIn removal throws; surface the message so scenarios can
            // assert on the refusal instead of crashing the command pipeline.
            return TextCommandResult.Error(ex.Message);
        }
    }
}
