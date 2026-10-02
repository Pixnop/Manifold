namespace AtlasFixture;

using System.Globalization;
using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

/// <summary>Sample enum value for the "flat" fixture dimension's "fixture-tint" metadata entry.</summary>
public enum FixtureTint
{
    Plain = 0,
    Painted = 7,
}

/// <summary>
/// Server-side fixture driven by the Manifold.Scenarios suite. All Manifold API
/// calls live here because scenario code cannot share assembly identity with the
/// ModLoader-loaded Manifold.dll. Results are published through SaveGame data.
/// </summary>
public sealed partial class AtlasFixtureModSystem : ModSystem
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

        // No boot pregeneration: it only grows every class's snapshot; scenarios use /atlasfx pregen or a transit.
        IDimension flat = _manifold.Registry
            .Define(new AssetLocation(Domain, "flat"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .WithMetadata("fixture-label", "granite-slab")
            .WithMetadata("fixture-level", 3)
            .WithMetadata("fixture-active", true)
            .WithMetadata("fixture-signature", new byte[] { 1, 2, 3 })
            .WithMetadata("fixture-tint", FixtureTint.Painted)
            .WithMetadata("fixture-note", null)
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

        IDimension pregenerated = _manifold.Registry
            .Define(new AssetLocation(Domain, "pregenerated"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(0)
            .RegisterStatic();
        PublishDimensionId("pregenerated", pregenerated.InternalId);

        // Dedicated to ColumnGeneratedEventScenarios: never pregenerated at boot (unlike
        // "pregenerated" above), so its one column only ever generates once the ColumnGenerated
        // subscriber below (registered in StartCoverageFixtures, which runs after this) is live.
        IDimension colgen = _manifold.Registry
            .Define(new AssetLocation(Domain, "colgen"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(0)
            .RegisterStatic();
        PublishDimensionId("colgen", colgen.InternalId);

        // Real vanilla terrain for RealTerrainLandingScenarios: seven known columns built by
        // TerrainProbeWorldgen (see its class doc for the layout), proving
        // TargetPositionResolvers.SameXZSurfaceY against the actual engine. Generation radius 0
        // keeps it to the single chunk the columns live in, same as "pregenerated" above; no
        // WithFixedSpawn, so it keeps the default SpawnBehavior.SameCoordinates the columns are
        // built to exercise.
        IDimension terrain = _manifold.Registry
            .Define(new AssetLocation(Domain, "terrain"))
            .Persistent()
            .WithWorldgen(new TerrainProbeWorldgen())
            .WithGenerationRadius(0)
            .RegisterStatic();
        PublishDimensionId("terrain", terrain.InternalId);

        // Exercises IManifoldServer.GenerateRegion called synchronously right here, right after
        // RegisterStatic, with no player and no transit (issue #69: a statically registered
        // dimension otherwise has no terrain until something visits it). Radius 0 keeps this to a
        // single chunk column so it does not meaningfully grow this class's snapshot.
        _manifold.GenerateRegion(pregenerated.Code, FixedSpawn);

        _manifold.Transitions.PlayerEntered += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-entered:{e.TargetDimension.Code.Path}", new[] { (byte)1 });
        _manifold.Transitions.PlayerLeft += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:player-left:{e.SourceDimension.Code.Path}", new[] { (byte)1 });

        RegisterCommands(api);
        RegisterOriginCommands(api);
        var config = api.LoadModConfig<AtlasFixtureConfig>("atlasfixture.json") ?? new();
        SeedPersistenceFixtures(config);
        SeedOriginFixtures(config, flat.InternalId);
        StartCoverageFixtures(api, config);
        StartSchemaFixtures(config);
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
    private void SeedPersistenceFixtures(AtlasFixtureConfig config)
    {
        if (!config.SeedPersistenceFixtures
            || _manifold.Registry.Get(new AssetLocation(Domain, "keeper")) is not null)
        {
            return;
        }

        // Runtime PERSISTENT dimension: must ride the manifest through a restart (as Pending).
        CreatePersistent("keeper");

        // Runtime EPHEMERAL dimension: must NOT survive a restart. No spawn pregeneration here
        // (unlike /atlasfx create-ephemeral): creation must load no chunks at boot.
        IDimension ghost = DefineSlab("ghost").Ephemeral().Create();
        PublishDimensionId("ghost", ghost.InternalId);
    }

    /// <summary>Base builder for a fixture dimension: granite-slab worldgen, the fixed spawn, generation radius 1.</summary>
    private IDimensionBuilder DefineSlab(string path) =>
        _manifold.Registry
            .Define(new AssetLocation(Domain, path))
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1);

    /// <summary>
    /// Creates a RUNTIME persistent dimension, or re-claims it after a restart (the same
    /// Define(...).Create() call promotes an already manifest-seeded Pending entry back to
    /// Active while keeping its manifest id). Shared by the boot seed and OnCreatePersistent.
    /// </summary>
    private IDimension CreatePersistent(string path)
    {
        IDimension dimension = DefineSlab(path)
            .Persistent()
            .WithMetadata("fixture-label", "runtime-persistent")
            .Create();
        PublishDimensionId(path, dimension.InternalId);
        return dimension;
    }

    /// <summary>
    /// RegisterStatic/Create only records the dimension; it does not generate any terrain. Manifold's
    /// active worldgen driver otherwise only runs through a player transit or join, so nothing
    /// generates a boot-registered dimension's spawn region on its own. IManifoldServer.GenerateRegion
    /// covers exactly this (issue #69). Invoked on demand through /atlasfx pregen, not at boot -
    /// except for the "pregenerated" dimension, which calls it directly from StartServerSide.
    /// </summary>
    private void PregenerateSpawn(IDimension dimension)
    {
        try
        {
            _manifold.GenerateRegion(dimension.Code, FixedSpawn);
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
            .BeginSubCommand("teleport-entity-plain")
                .WithArgs(parsers.Word("entityid"), parsers.Word("dimpath"))
                .HandleWith(OnTeleportEntityPlain)
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
            .BeginSubCommand("mount-player")
                .WithArgs(parsers.Word("playername"), parsers.Word("entityid"))
                .HandleWith(OnMountPlayer)
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
            : dimPath.Contains(':')
                ? new AssetLocation(dimPath)
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

    /// <summary>
    /// Entity transit with no options at all, so the default resolver
    /// (TargetPositionResolvers.SameXZSurfaceY) decides the landing, same as OnTeleportEntity
    /// but without the OverridePosition that masks it. Drives RealTerrainLandingScenarios.
    /// </summary>
    private TextCommandResult OnTeleportEntityPlain(TextCommandCallingArgs args)
    {
        long entityId = long.Parse((string)args[0], CultureInfo.InvariantCulture);
        var dimPath = (string)args[1];

        Entity? entity = _sapi.World.GetEntityById(entityId);
        if (entity is null)
        {
            return TextCommandResult.Error($"No entity with id {entityId}.");
        }

        try
        {
            _manifold.Transitions.TeleportEntity(entity, ResolveTargetCode(dimPath));
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

        IServerPlayer? player = FindPlayer(playerName);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {playerName}.");
        }

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

    /// <summary>
    /// Mounts <c>playername</c> onto the seat of the already-spawned mountable entity
    /// <c>entityid</c> (a boat, a saddled creature). Base-game mounting, not Manifold's, this is
    /// only here so PlayerTransitScenarios can set up a rider to teleport, without the scenario
    /// project itself needing a GameContent reference for one seat call.
    /// </summary>
    private TextCommandResult OnMountPlayer(TextCommandCallingArgs args)
    {
        var playerName = (string)args[0];
        long entityId = long.Parse((string)args[1], CultureInfo.InvariantCulture);

        IServerPlayer? player = FindPlayer(playerName);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {playerName}.");
        }

        Entity? mount = _sapi.World.GetEntityById(entityId);
        if (mount is null)
        {
            return TextCommandResult.Error($"No entity with id {entityId}.");
        }

        EntityBehaviorSeatable? seatable = mount.GetBehavior<EntityBehaviorSeatable>();
        if (seatable is null)
        {
            return TextCommandResult.Error($"Entity {entityId} has no seatable behavior.");
        }

        return seatable.TryMount(player.Entity)
            ? TextCommandResult.Success("mounted")
            : TextCommandResult.Error("The seat refused to mount the player.");
    }

    private TextCommandResult OnTeleportBlock(TextCommandCallingArgs args)
    {
        var source = new BlockPos((int)args[0], (int)args[1], (int)args[2], (int)args[3]);
        var targetLocal = new BlockPos((int)args[5], (int)args[6], (int)args[7], 0);

        try
        {
            bool moved = _manifold.Transitions.TeleportBlock(source, ResolveTargetCode((string)args[4]), targetLocal);
            if (moved)
            {
                return TextCommandResult.Success("moved");
            }

            // Tells apart the two ways TeleportBlock can no-op: source was air, or source is part of
            // a multi-position structure (multiblock/door/bed) and was refused. Also exercises
            // IsMultiPositionBlock as the pre-check callers are meant to use.
            bool refused = _manifold.Transitions.IsMultiPositionBlock(source);
            return TextCommandResult.Success(refused ? "refused" : "no-op");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private TextCommandResult OnCreateEphemeral(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension dimension = DefineSlab(path)
            .WithMetadata("fixture-tint", FixtureTint.Painted)
            .Ephemeral()
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
            IDimension dimension = CreatePersistent(path);
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
        IDimension? dimension = _manifold.Registry.Get(ResolveTargetCode(path));
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
