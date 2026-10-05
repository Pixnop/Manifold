namespace AtlasFixture;

using System.Linq;
using System.Text;
using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

/// <summary>
/// Fixture surface for the admin, lifecycle, event, travel-policy, command-builder and quarantine
/// scenarios. Boot registration here, like the rest of the fixture, loads no chunks: every
/// dimension below generates on its first transit.
/// </summary>
public sealed partial class AtlasFixtureModSystem
{
    /// <summary>SaveGame key of the ordered transit/registry event log ("kind:detail|kind:detail|...").</summary>
    internal const string EventLogKey = Domain + ":eventlog";

    /// <summary>Code of the manifest entry injected for the quarantine scenarios.</summary>
    internal const string OrphanCode = "atlasghost:orphan";

    /// <summary>
    /// Block the ColumnGenerated subscriber marks generated columns with in "colgen": distinct from
    /// GraniteSlabWorldgen's granite so ColumnGeneratedEventScenarios can tell the marker apart from
    /// ordinary generated terrain.
    /// </summary>
    internal const string ColumnMarkerBlockCode = "game:rock-basalt";

    private const int OrphanInternalId = 900;

    private void StartCoverageFixtures(ICoreServerAPI api, AtlasFixtureConfig config)
    {
        RegisterPolicyDimension("anchor", b => b.WithSpawnBehavior(SpawnBehavior.DimensionSpawn));
        RegisterPolicyDimension("memory", b => b.WithSpawnBehavior(SpawnBehavior.LastVisited));
        RegisterPolicyDimension("creative", b => b.WithForcedGameMode(EnumGameMode.Creative));
        RegisterPolicyDimension("locked", b => b.WithMetadata("atlas-veto", "entering"));
        RegisterPolicyDimension("gate", b => b.WithMetadata("atlas-veto", "arriving"));
        RegisterPolicyDimension("faulty", b => b);

        // RespawnScenarios: a dimension that keeps its dead (respawn at its fixed spawn), and one that
        // asks for that without having a spawn point, which must fall back to the overworld.
        RegisterPolicyDimension("bunker", b => b.WithRespawnBehavior(RespawnBehavior.DimensionSpawn));
        IDimension nospawn = _manifold.Registry
            .Define(new AssetLocation(Domain, "nospawn"))
            .Persistent()
            .WithWorldgen(new GraniteSlabWorldgen())
            .WithRespawnBehavior(RespawnBehavior.DimensionSpawn)
            .RegisterStatic();
        PublishDimensionId("nospawn", nospawn.InternalId);

        // Registered before the recording handlers on purpose: a subscriber that throws must not
        // keep the ones after it from running (Manifold isolates each subscriber).
        _manifold.Transitions.PlayerEntered += (_, e) =>
        {
            if (e.TargetDimension.Code.Path == "faulty")
            {
                throw new InvalidOperationException("Deliberately faulty subscriber (Atlas fixture).");
            }
        };

        _manifold.Transitions.PlayerEntering += (_, e) =>
        {
            AppendEvent($"entering:{e.TargetDimension.Code.Path}");
            e.Cancel = e.TargetDimension.GetMetadata<string>("atlas-veto") == "entering";
        };
        _manifold.Transitions.PlayerArriving += (_, e) =>
        {
            AppendEvent($"arriving:{e.TargetDimension.Code.Path}");
            if (e.TargetDimension.GetMetadata<string>("atlas-veto") == "arriving")
            {
                e.Cancel = true;
                e.CancellationReason = "Atlas fixture veto.";
            }
        };
        _manifold.Transitions.PlayerLeft += (_, e) =>
            AppendEvent($"left:{e.SourceDimension.Code.Path}->{e.TargetDimension.Code.Path}");
        _manifold.Transitions.PlayerEntered += (_, e) =>
        {
            AppendEvent($"entered:{e.TargetDimension.Code.Path}");
            if (e.IsRespawn)
            {
                AppendEvent($"respawn:{e.TargetDimension.Code.Path}");
            }
        };
        _manifold.Transitions.EntityChangedDimension += (_, e) =>
            AppendEvent($"entity:{e.PreviousDimension.Code.Path}->{e.NewDimension.Code.Path}");
        _manifold.Registry.Created += (_, e) => AppendEvent($"created:{e.Dimension.Code.Path}");
        _manifold.Registry.Destroyed += (_, e) => AppendEvent($"destroyed:{e.Dimension.Code.Path}");

        // Drives ColumnGeneratedEventScenarios: marks the center of every column ColumnGenerated
        // fires for in "colgen" with a block GraniteSlabWorldgen never places, so the scenario can
        // tell the marker apart from ordinary generated terrain.
        _manifold.Registry.ColumnGenerated += (_, e) =>
        {
            if (e.Dimension.Code.Path != "colgen")
            {
                return;
            }

            int markerBlockId = _sapi.World.GetBlock(new AssetLocation(ColumnMarkerBlockCode))!.BlockId;
            var markerPos = new BlockPos((e.ChunkX * 32) + 16, 10, (e.ChunkZ * 32) + 16, e.Dimension.InternalId);
            e.BlockAccessor.SetBlock(markerBlockId, markerPos);
        };

        new DimensionCommandBuilder()
            .Command("atlasgo")
            .TargetDimension(new AssetLocation(Domain, "anchor"))
            .RequiresPrivilege(Privilege.chat)
            .DescribedAs("Atlas fixture: transit to the anchor dimension.")
            .Register(api);
        new DimensionCommandBuilder()
            .Command("atlasgoadmin")
            .TargetDimension(new AssetLocation(Domain, "anchor"))
            .RequiresPrivilege(Privilege.controlserver)
            .DescribedAs("Atlas fixture: admin-only transit to the anchor dimension.")
            .Register(api);

        RegisterCoverageCommands(api);

        if (config.SeedOrphan)
        {
            // Manifold rewrites the manifest from its registry on GameWorldSave; its handler was
            // registered earlier (lower ExecuteOrder), so this one runs after it and the entry
            // survives into the save the next boot reads.
            api.Event.GameWorldSave += InjectOrphanManifestEntry;
        }
    }

    private void RegisterPolicyDimension(string path, Func<IDimensionBuilder, IDimensionBuilder> configure)
    {
        IDimension dimension = configure(DefineSlab(path).Persistent()).RegisterStatic();
        PublishDimensionId(path, dimension.InternalId);
    }

    private void AppendEvent(string entry)
    {
        byte[]? current = _sapi.WorldManager.SaveGame.GetData(EventLogKey);
        string log = current is null or { Length: 0 } ? entry : Encoding.UTF8.GetString(current) + "|" + entry;
        _sapi.WorldManager.SaveGame.StoreData(EventLogKey, Encoding.UTF8.GetBytes(log));
    }

    /// <summary>
    /// Appends a Persistent manifest entry owned by a mod that is not installed. On the next boot
    /// Manifold seeds it from the manifest, finds no owner, and quarantines it.
    /// </summary>
    private void InjectOrphanManifestEntry()
    {
        var tree = new TreeAttribute();
        byte[]? raw = _sapi.WorldManager.SaveGame.GetData("manifold:manifest");
        if (raw is { Length: > 0 })
        {
            tree.FromBytes(raw);
        }

        ITreeAttribute entries = tree.GetOrAddTreeAttribute("entries");
        if (entries.Select(kvp => kvp.Value).OfType<ITreeAttribute>().Any(e => e.GetString("code") == OrphanCode))
        {
            return;
        }

        var orphan = new TreeAttribute();
        orphan.SetString("code", OrphanCode);
        orphan.SetInt("id", OrphanInternalId);
        orphan.SetInt("lifetime", (int)DimensionLifetime.Persistent);
        orphan.SetString("owner", "atlasghost");
        entries["orphan"] = orphan;
        _sapi.WorldManager.SaveGame.StoreData("manifold:manifest", tree.ToBytes());
    }

    private void RegisterCoverageCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create("atlasfx2")
            .WithDescription("Drives Manifold's admin, lifecycle and policy surface for Atlas scenarios.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("teleport-player-plain")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"))
                .HandleWith(OnTeleportPlayerPlain)
            .EndSubCommand()
            .BeginSubCommand("create-darksky")
                .WithArgs(parsers.Word("dimpath"), parsers.Int("ceiling"))
                .HandleWith(OnCreateDarkSky)
            .EndSubCommand()
            .BeginSubCommand("create-streaming")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnCreateStreaming)
            .EndSubCommand()
            .BeginSubCommand("relight-region")
                .WithArgs(
                    parsers.Word("dimpath"),
                    parsers.Int("x1"),
                    parsers.Int("y1"),
                    parsers.Int("z1"),
                    parsers.Int("x2"),
                    parsers.Int("y2"),
                    parsers.Int("z2"))
                .HandleWith(OnRelightRegion)
            .EndSubCommand()
            .BeginSubCommand("force-remove")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnForceRemove)
            .EndSubCommand()
            .BeginSubCommand("kick")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnKick)
            .EndSubCommand()
            .BeginSubCommand("try-teleport-player")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"))
                .HandleWith(OnTryTeleportPlayer)
            .EndSubCommand()
            .BeginSubCommand("players-in")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnPlayersIn)
            .EndSubCommand()
            .BeginSubCommand("dimension-of")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnDimensionOf)
            .EndSubCommand();
    }

    /// <summary>Drives the public ITransitionService.TryTeleportPlayer and reports its bool result directly.</summary>
    private TextCommandResult OnTryTeleportPlayer(TextCommandCallingArgs args)
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
            bool moved = _manifold.Transitions.TryTeleportPlayer(player, ResolveTargetCode(dimPath), options);
            return TextCommandResult.Success(moved ? "moved" : "cancelled");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Drives IManifoldServer.GetPlayersIn, reporting occupant names joined by comma, or "none".</summary>
    private TextCommandResult OnPlayersIn(TextCommandCallingArgs args)
    {
        var dimPath = (string)args[0];
        try
        {
            var players = _manifold.GetPlayersIn(ResolveTargetCode(dimPath));
            return TextCommandResult.Success(
                players.Count == 0 ? "none" : string.Join(",", players.Select(p => p.PlayerName)));
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Drives IDimensionRegistry.GetDimensionOf, reporting the dimension's code or "unregistered".</summary>
    private TextCommandResult OnDimensionOf(TextCommandCallingArgs args)
    {
        var playerName = (string)args[0];
        IServerPlayer? player = FindPlayer(playerName);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {playerName}.");
        }

        IDimension? dimension = _manifold.Registry.GetDimensionOf(player.Entity);
        return TextCommandResult.Success(dimension is null ? "unregistered" : dimension.Code.ToString());
    }

    private IServerPlayer? FindPlayer(string name) =>
        _sapi.World.AllOnlinePlayers.OfType<IServerPlayer>().FirstOrDefault(p => p.PlayerName == name);

    /// <summary>Transit with no options at all, so the dimension's own spawn behavior decides the landing.</summary>
    private TextCommandResult OnTeleportPlayerPlain(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        try
        {
            _manifold.Transitions.TeleportPlayer(player, ResolveTargetCode((string)args[1]));
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

        return TextCommandResult.Success("ok");
    }

    private TextCommandResult OnCreateDarkSky(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension dimension = DefineSlab(path).Ephemeral().WithDarkSky((int)args[1]).Create();
        PublishDimensionId(path, dimension.InternalId);
        PregenerateSpawn(dimension);
        return TextCommandResult.Success($"created {dimension.InternalId}");
    }

    /// <summary>Creates an ephemeral STREAMING slab dimension (loadRadius 2, widened to the radius the engine sends each player).</summary>
    private TextCommandResult OnCreateStreaming(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension dimension = DefineSlab(path).Ephemeral().Streaming(2).Create();
        PublishDimensionId(path, dimension.InternalId);
        PregenerateSpawn(dimension);
        return TextCommandResult.Success($"created {dimension.InternalId}");
    }

    /// <summary>Drives the public IManifoldServer.RelightRegion over the given box.</summary>
    private TextCommandResult OnRelightRegion(TextCommandCallingArgs args)
    {
        var min = new BlockPos((int)args[1], (int)args[2], (int)args[3], 0);
        var max = new BlockPos((int)args[4], (int)args[5], (int)args[6], 0);
        try
        {
            _manifold.RelightRegion(ResolveTargetCode((string)args[0]), min, max);
            return TextCommandResult.Success("relit");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private TextCommandResult OnForceRemove(TextCommandCallingArgs args)
    {
        try
        {
            bool removed = _manifold.ForceRemoveDimension(new AssetLocation(Domain, (string)args[0]));
            return TextCommandResult.Success(removed ? "removed" : "not-removed");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private TextCommandResult OnKick(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        player.Disconnect("Kicked by the Atlas fixture.");
        return TextCommandResult.Success("kicked");
    }
}
