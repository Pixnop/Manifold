namespace CompatFixture;

using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

/// <summary>
/// Server-side fixture for the upgrade/downgrade compatibility scenarios
/// (tests/Manifold.Scenarios.CompatFixtures generates the two fixture saves,
/// tests/Manifold.Scenarios.Compat verifies them). Compiled ONLY against the published
/// Pixnop.Manifold 0.5.1 NuGet package (see the csproj comment): every call here is one a mod
/// author who last built against 0.5.1 could make, whether the dimension it is staged next to is
/// the published 0.5.1 release zip or this repo's dev build. Manifold's AssemblyVersion is frozen
/// (Directory.Build.props) precisely so this single compiled fixture keeps loading against both.
///
/// Registers one static persistent dimension, "manicompat:compat", combining every persisted
/// shape the compatibility scenarios care about: LastVisited spawn behavior (per-player position
/// memory), a forced game mode (per-player prior-mode save/restore), and a separate inventory
/// profile (per-player, per-dimension item set). Its terrain and any block a scenario places in
/// it are the "generated terrain intact" proof.
/// </summary>
public sealed class CompatFixtureModSystem : ModSystem
{
    internal const string Domain = "manicompat";
    internal const string DimensionPath = "compat";

    private static readonly AssetLocation DimensionCode = new(Domain, DimensionPath);
    private static readonly AssetLocation FillerDimensionCode = new(Domain, "filler");
    private static readonly AssetLocation OverworldCode = new("manifold", "overworld");

    private ICoreServerAPI _sapi = null!;
    private IManifoldServer _manifold = null!;

    // Run after Manifold (0.05), like any consumer mod.
    public override double ExecuteOrder() => 0.5;

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        _sapi = api;
        _manifold = api.GetManifoldServer(this);

        // Snapshot the id this dimension was published under before this boot's own
        // RegisterStatic below overwrites it, so a verifier scenario loading a save produced by
        // a DIFFERENT Manifold build (dev vs. the 0.5.1 release) can assert identity was kept
        // across the version change, not just across an ordinary restart.
        byte[]? previous = _sapi.WorldManager.SaveGame.GetData($"{Domain}:dimid");
        if (previous is not null)
        {
            _sapi.WorldManager.SaveGame.StoreData($"{Domain}:prevdimid", previous);
        }

        // On the first ever boot only (no prior dimid), reserve one EPHEMERAL filler dimension
        // before "compat" registers, so first-fit hands the filler the lower id and "compat" the
        // next one up, never id 10 itself. Ephemeral dimensions are never written to the
        // manifest, so this filler leaves a PERMANENT gap at its id: every later boot (including
        // every reload of a committed fixture) has nothing reserved there at all, because nothing
        // ever asks for it again. This is what makes the "same internal id" check below actually
        // prove the manifest round-tripped "compat"'s id, rather than merely reproduce a
        // coincidence: with only "compat" ever persistently registered, a first-fit allocator
        // asked for a FRESH id (the failure mode this filler exists to catch: "compat"'s own
        // manifest entry lost, RegisterStatic falling through to Reserve() instead of recovering
        // its old id) would always land back on the exact same id "compat" already has, id 10,
        // since nothing else is ever reserved to skip past. With the gap in place, that same
        // fresh-allocation fallback lands on the gap's id instead, different from "compat"'s
        // real, manifest-preserved id, so a lost manifest entry now actually fails this
        // assertion instead of passing it by coincidence.
        if (previous is null)
        {
            _manifold.Registry
                .Define(FillerDimensionCode)
                .Ephemeral()
                .WithWorldgen(new SlabWorldgen())
                .WithGenerationRadius(0)
                .Create();
        }

        IDimension compat = _manifold.Registry
            .Define(DimensionCode)
            .Persistent()
            .WithWorldgen(new SlabWorldgen())
            .WithGenerationRadius(1)
            .WithSpawnBehavior(SpawnBehavior.LastVisited)
            .WithForcedGameMode(EnumGameMode.Creative)
            .WithSeparateInventory(ManifoldInventory.All)
            .RegisterStatic();

        _sapi.WorldManager.SaveGame.StoreData($"{Domain}:dimid", BitConverter.GetBytes(compat.InternalId));

        RegisterCommands(api);
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create(Domain)
            .WithDescription("Drives the Manifold 0.5.1 API for the upgrade/downgrade compatibility scenarios.")
            .RequiresPrivilege("controlserver")
            .BeginSubCommand("enter")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnEnter)
            .EndSubCommand()
            .BeginSubCommand("leave")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnLeave)
            .EndSubCommand();
    }

    /// <summary>
    /// Teleports the player into "compat" with NO position override, so the dimension's own
    /// LastVisited spawn behavior decides the landing spot (same coordinates on a first visit,
    /// the player's own last-recorded position on a later one) and entry forces Creative,
    /// saving whatever mode the player was in.
    /// </summary>
    private TextCommandResult OnEnter(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {(string)args[0]}.");
        }

        _manifold.Transitions.TeleportPlayer(player, DimensionCode);
        return TextCommandResult.Success("ok");
    }

    /// <summary>Teleports the player back to the built-in overworld, restoring their pre-forced game mode.</summary>
    private TextCommandResult OnLeave(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {(string)args[0]}.");
        }

        _manifold.Transitions.TeleportPlayer(player, OverworldCode);
        return TextCommandResult.Success("ok");
    }

    private IServerPlayer? FindPlayer(string name) =>
        Array.Find(_sapi.World.AllOnlinePlayers, p => p.PlayerName == name) as IServerPlayer;

    /// <summary>Fills y 1..4 of every column with granite. Deterministic, and distinct from air so a scenario-placed block stands out.</summary>
    private sealed class SlabWorldgen : Manifold.Api.Worldgen.IWorldgenStrategy
    {
        private int _graniteBlockId;

        public void OnInitialize(Manifold.Api.Worldgen.IWorldgenInitContext ctx)
        {
            _graniteBlockId = ctx.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;
        }

        public void GenerateColumn(Manifold.Api.Worldgen.IWorldgenChunkContext ctx)
        {
            for (int localX = 0; localX < 32; localX++)
            {
                for (int localZ = 0; localZ < 32; localZ++)
                {
                    for (int y = 1; y <= 4; y++)
                    {
                        var pos = new BlockPos(
                            (ctx.ChunkX * 32) + localX,
                            y,
                            (ctx.ChunkZ * 32) + localZ,
                            ctx.DimensionId);
                        ctx.BlockAccessor.SetBlock(_graniteBlockId, pos);
                    }
                }
            }
        }
    }
}
