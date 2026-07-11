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

        // No PregenerateSpawn at boot. Since Atlas 0.8.0 (rollback stage 3) loaded
        // mini-dimension columns are simply part of the world snapshot, so boot-time
        // pregeneration would no longer disqualify rollback; skipping it is now a
        // snapshot-size optimization, not a requirement. Registration alone loads
        // nothing; scenarios that need a dimension's terrain request generation on
        // demand via /atlasfx pregen <dimpath> or trigger it through a transit.
        IDimension flat = _manifold.Registry
            .Define(new AssetLocation(Domain, "flat"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .WithMetadata("fixture-label", "granite-slab")
            .WithMetadata("fixture-level", 3)
            .RegisterStatic();
        PublishDimensionId("flat", flat.InternalId);

        IDimension voidDim = _manifold.Registry
            .Define(new AssetLocation(Domain, "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .RegisterStatic();
        PublishDimensionId("void", voidDim.InternalId);

        IDimension vault = _manifold.Registry
            .Define(new AssetLocation(Domain, "vault"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .WithSeparateInventory(ManifoldInventory.All)
            .RegisterStatic();
        PublishDimensionId("vault", vault.InternalId);

        IDimension stream = _manifold.Registry
            .Define(new AssetLocation(Domain, "stream"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .Streaming(2)
            .WithStreamingBudget(1)
            .RegisterStatic();
        PublishDimensionId("stream", stream.InternalId);

        _manifold.Transitions.PlayerEntered += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-entered:{e.TargetDimension.Code.Path}", new[] { (byte)1 });
        _manifold.Transitions.PlayerLeft += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-left:{e.SourceDimension.Code.Path}", new[] { (byte)1 });

        RegisterCommands(api);
        SeedPersistenceFixtures();
    }

    private void PublishDimensionId(string path, int internalId)
    {
        // Keep the previous boot's published id (if any) under a separate key first: restart
        // scenarios compare it against the fresh id to assert identity stability across a real
        // save/load round trip, which a single overwritten key could not show.
        byte[]? previous = _sapi.WorldManager.SaveGame.GetData($"{Domain}:dimid:{path}");
        if (previous is not null)
        {
            _sapi.WorldManager.SaveGame.StoreData($"{Domain}:prevdimid:{path}", previous);
        }

        _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:dimid:{path}",
            BitConverter.GetBytes(internalId));
    }

    /// <summary>
    /// Boot-time seeding for DimensionPersistenceScenarios, requested through the ModConfig
    /// file that scenario class stages with [AtlasDataFiles]. Seeding at BOOT rather than in a
    /// seed scenario keeps the persistence scenarios order-independent: Atlas does not
    /// guarantee scenario order within a class, but every RestartWorld scenario finds this
    /// state in the manifest no matter which runs first. Gated on the registry, not a flag: on
    /// the first boot 'keeper' is unknown and gets created; on a restarted boot it is already
    /// back from the manifest as Pending, and re-creating it here would promote it to Active
    /// and destroy exactly the state the scenarios assert on. Registration loads no chunks, so
    /// this seeding cannot degrade any rollback; classes without the config file skip it.
    /// </summary>
    private void SeedPersistenceFixtures()
    {
        AtlasFixtureConfig? config = _sapi.LoadModConfig<AtlasFixtureConfig>("atlasfixture.json");
        if (config is not { SeedPersistenceFixtures: true }
            || _manifold.Registry.Get(new AssetLocation(Domain, "keeper")) is not null)
        {
            return;
        }

        // Runtime PERSISTENT dimension: must ride the manifest through a restart (as Pending).
        IDimension keeper = _manifold.Registry
            .Define(new AssetLocation(Domain, "keeper"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1)
            .WithMetadata("fixture-label", "runtime-persistent")
            .Create();
        PublishDimensionId("keeper", keeper.InternalId);

        // Runtime EPHEMERAL dimension: must NOT survive a restart. No spawn pregeneration here
        // (unlike /atlasfx create-ephemeral): creation must load no chunks at boot.
        IDimension ghost = _manifold.Registry
            .Define(new AssetLocation(Domain, "ghost"))
            .Ephemeral()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1)
            .Create();
        PublishDimensionId("ghost", ghost.InternalId);
    }

    /// <summary>
    /// RegisterStatic only records the dimension; it does not generate any terrain. Manifold's
    /// active worldgen driver only runs through Transitions (player transit / join), so nothing
    /// generates a boot-registered dimension's spawn region on its own. Force it here via a no-op
    /// TeleportBlock: this call exists purely for its generate-destination-region side effect via
    /// DimensionGenerator.EnsureRegion, not for the move itself. The source position must be air
    /// so the move is a guaranteed no-op; a near-ceiling position at the world origin is reliably
    /// air, unlike y=1 near bedrock, and using a non-air source would actually move a real
    /// overworld block. Invoked on demand through /atlasfx pregen, not at boot: since Atlas
    /// 0.8.0 boot-time pregeneration would no longer disqualify rollback, it would just grow
    /// every scenario class's snapshot (see the StartServerSide note).
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
            .BeginSubCommand("create-persistent")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnCreatePersistent)
            .EndSubCommand()
            .BeginSubCommand("state")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnState)
            .EndSubCommand()
            .BeginSubCommand("remove")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnRemove)
            .EndSubCommand()
            .BeginSubCommand("teleport-player")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"))
                .HandleWith(OnTeleportPlayer)
            .EndSubCommand()
            .BeginSubCommand("pregen")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnPregen)
            .EndSubCommand()
            .BeginSubCommand("metadata")
                .WithArgs(parsers.Word("dimpath"), parsers.Word("key"))
                .HandleWith(OnMetadata)
            .EndSubCommand();
    }

    /// <summary>
    /// Resolves a scenario-facing dimension path to a registered code: "overworld" maps to the
    /// built-in manifold:overworld so scenarios can transit back to dimension 0; everything else
    /// is a fixture dimension under the atlasfixture domain.
    /// </summary>
    private static AssetLocation ResolveTargetCode(string dimPath) =>
        dimPath == "overworld"
            ? new AssetLocation("manifold", "overworld")
            : new AssetLocation(Domain, dimPath);

    /// <summary>
    /// Landing position for transits driven by this fixture: the vanilla default spawn for the
    /// overworld, the air pocket two blocks below the fixed spawn for fixture dimensions. The
    /// BlockPos dimension stays 0 in the fixture-dimension case; the transit target dimension
    /// comes from the target code.
    /// </summary>
    private BlockPos DefaultLanding(string dimPath) =>
        dimPath == "overworld"
            ? _sapi.World.DefaultSpawnPosition.AsBlockPos
            : new BlockPos(FixedSpawn.X, FixedSpawn.Y - 2, FixedSpawn.Z, 0);

    private TextCommandResult OnTeleportEntity(TextCommandCallingArgs args)
    {
        long entityId = long.Parse((string)args[0], CultureInfo.InvariantCulture);
        var dimPath = (string)args[1];

        Entity? entity = _sapi.World.GetEntityById(entityId);
        if (entity is null)
        {
            return TextCommandResult.Error($"No entity with id {entityId}.");
        }

        // Land inside the generated area around a fixture dimension's fixed spawn (two blocks
        // below the fixed spawn's Y, matching the granite slab's air pocket), or at the vanilla
        // default spawn when returning to the overworld.
        var options = new TransitionOptions { OverridePosition = DefaultLanding(dimPath) };
        try
        {
            _manifold.Transitions.TeleportEntity(entity, ResolveTargetCode(dimPath), options);
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

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

        // AsBlockPos carries the EntityPos dimension through on the overworld branch; the
        // vanilla default spawn is dimension 0.
        var options = new TransitionOptions { OverridePosition = DefaultLanding(dimPath) };
        try
        {
            _manifold.Transitions.TeleportPlayer(player, ResolveTargetCode(dimPath), options);
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

        return TextCommandResult.Success("ok");
    }

    private TextCommandResult OnTeleportBlock(TextCommandCallingArgs args)
    {
        var source = new BlockPos((int)args[0], (int)args[1], (int)args[2], (int)args[3]);
        var targetLocal = new BlockPos((int)args[5], (int)args[6], (int)args[7], 0);

        try
        {
            bool moved = _manifold.Transitions.TeleportBlock(source, ResolveTargetCode((string)args[4]), targetLocal);
            return TextCommandResult.Success(moved ? "moved" : "no-op");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
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

    /// <summary>
    /// Creates a RUNTIME persistent dimension, or re-claims it after a server restart. On a
    /// first boot the code is unknown and Create() allocates a fresh id; after a restart the
    /// manifest has seeded the same code as Pending, and the same Define(...).Create() call
    /// promotes it back to Active while keeping its manifest id (the owner-reclaim path).
    /// Driven by DimensionPersistenceScenarios.
    /// </summary>
    private TextCommandResult OnCreatePersistent(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        try
        {
            IDimension dimension = _manifold.Registry
                .Define(new AssetLocation(Domain, path))
                .Persistent()
                .WithWorldgen(new GraniteSlabWorldgen())
                .WithFixedSpawn(FixedSpawn)
                .WithGenerationRadius(1)
                .WithMetadata("fixture-label", "runtime-persistent")
                .Create();
            PublishDimensionId(path, dimension.InternalId);
            return TextCommandResult.Success($"created {dimension.InternalId}");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports a dimension's registry state and internal id as "State:id" (e.g. "Active:11",
    /// "Pending:12"), or the error "unregistered" when the code is not in the registry at all.
    /// The persistence scenarios use this to tell apart the three post-restart fates: re-claimed
    /// static dimensions (Active), manifest-seeded runtime ones (Pending), and ephemeral ones
    /// (unregistered).
    /// </summary>
    private TextCommandResult OnState(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension? dimension = _manifold.Registry.Get(new AssetLocation(Domain, path));
        if (dimension is null)
        {
            return TextCommandResult.Error("unregistered");
        }

        return TextCommandResult.Success(
            string.Create(CultureInfo.InvariantCulture, $"{dimension.State}:{dimension.InternalId}"));
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
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private TextCommandResult OnPregen(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension? dimension = _manifold.Registry.Get(new AssetLocation(Domain, path));
        if (dimension is null)
        {
            return TextCommandResult.Error($"No dimension registered under '{path}'.");
        }

        PregenerateSpawn(dimension);
        return TextCommandResult.Success("pregenerated");
    }

    private TextCommandResult OnMetadata(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        var key = (string)args[1];
        IDimension? dimension = _manifold.Registry.Get(new AssetLocation(Domain, path));
        if (dimension is null)
        {
            return TextCommandResult.Error($"No dimension registered under '{path}'.");
        }

        if (!dimension.Metadata.TryGetValue(key, out object? value))
        {
            return TextCommandResult.Error("missing");
        }

        return TextCommandResult.Success(
            value is null ? "null" : string.Create(CultureInfo.InvariantCulture, $"{value.GetType().Name}:{value}"));
    }
}
