namespace AtlasFixture;

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Manifold.Api;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

/// <summary>
/// Fixture surface for the arrival-yaw and return-to-origin scenarios: commands that drive
/// ITransitionService.GetOrigin / TryReturnPlayer and the Yaw transition option, and the boot-time
/// seeding of an origin blob for the restart scenario.
/// </summary>
public sealed partial class AtlasFixtureModSystem
{
    /// <summary>Uid of the player whose origin is seeded at boot (Atlas derives a test player's uid as "atlas-" plus the name).</summary>
    internal const string SeededOriginPlayerUid = "atlas-originkept";

    private const string OriginsKey = "manifold:origins";

    /// <summary>Savegame key <c>load-column</c> publishes once an overworld column is loaded (1 in its first byte).</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkZ">Chunk Z.</param>
    /// <returns>The key.</returns>
    internal static string ColumnLoadedKey(int chunkX, int chunkZ) => $"{Domain}:column:{chunkX}:{chunkZ}";

    /// <summary>Savegame key <c>chain-hop</c> publishes its outcome under, per player name.</summary>
    /// <param name="playerName">Player name.</param>
    /// <returns>The key.</returns>
    internal static string ChainHopKey(string playerName) => $"{Domain}:chainhop:{playerName}";

    private void RegisterOriginCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create("atlasfx3")
            .WithDescription("Drives Manifold's arrival yaw and return-to-origin surface for Atlas scenarios.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("teleport-player-yaw-at")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"), parsers.Double("yaw"), parsers.Int("x"), parsers.Int("z"))
                .HandleWith(OnTeleportPlayerYawAt)
            .EndSubCommand()
            .BeginSubCommand("hop-and-return")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"), parsers.Double("yaw"), parsers.Int("x"), parsers.Int("z"))
                .HandleWith(OnHopAndReturn)
            .EndSubCommand()
            .BeginSubCommand("chain-hop")
                .WithArgs(
                    parsers.Word("playername"),
                    parsers.Word("dimpatha"),
                    parsers.Int("ax"),
                    parsers.Int("az"),
                    parsers.Word("dimpathb"),
                    parsers.Int("bx"),
                    parsers.Int("bz"),
                    parsers.Word("dimpathc"))
                .HandleWith(OnChainHop)
            .EndSubCommand()
            .BeginSubCommand("load-column")
                .WithArgs(parsers.Int("x"), parsers.Int("z"))
                .HandleWith(OnLoadColumn)
            .EndSubCommand()
            .BeginSubCommand("double-hop")
                .WithArgs(
                    parsers.Word("playername"),
                    parsers.Word("dimpatha"),
                    parsers.Int("ax"),
                    parsers.Int("az"),
                    parsers.Word("dimpathb"),
                    parsers.Int("bx"),
                    parsers.Int("bz"))
                .HandleWith(OnDoubleHop)
            .EndSubCommand()
            .BeginSubCommand("origin")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnOrigin)
            .EndSubCommand()
            .BeginSubCommand("return")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(OnReturn)
            .EndSubCommand();
    }

    /// <summary>
    /// Writes, on the very first boot only, an origin blob (schema version 1, the layout
    /// PlayerOriginStore documents) for <see cref="SeededOriginPlayerUid"/> in the overworld: they
    /// came from the "flat" dimension, at an exact fractional position, facing a known yaw. Manifold
    /// loads it on the restarted boot. Raw bytes rather than a real transit because Atlas cannot
    /// restart a class that has joined players, so no player can transit before the restart; the
    /// SAVE half of the round trip is covered separately, by a real transit followed by a save.
    /// </summary>
    private void SeedOriginFixtures(AtlasFixtureConfig config, int flatInternalId)
    {
        if (!config.SeedOrigin || _sapi.WorldManager.SaveGame.GetData(OriginsKey) is { Length: > 0 })
        {
            return;
        }

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1);
            w.Write(SeededOriginPlayerUid + "|0");
            w.Write(flatInternalId);
            w.Write(Domain + ":flat");
            w.Write("manifold:overworld");
            w.Write(515.25);
            w.Write(6.0);
            w.Write(509.75);
            w.Write(1.25f);
        }

        _sapi.WorldManager.SaveGame.StoreData(OriginsKey, ms.ToArray());
    }

    /// <summary>
    /// Player transit with a Yaw option, landing at (x, 6, z) of the target. Reports "ok:" plus whether
    /// the engine is still waiting to apply the move (<c>Entity.Teleporting</c>): it waits when the
    /// overworld column at that X/Z is not loaded, which scenarios pick the coordinates for.
    /// </summary>
    private TextCommandResult OnTeleportPlayerYawAt(TextCommandCallingArgs args)
    {
        var options = new TransitionOptions
        {
            OverridePosition = new BlockPos((int)args[3], FixedSpawn.Y - 2, (int)args[4], 0),
            Yaw = (float)(double)args[2],
        };
        return TransitWith(args, options);
    }

    /// <summary>
    /// Loads, and keeps loaded, the overworld column at a block X/Z, then publishes
    /// <see cref="ColumnLoadedKey"/> when it is in. A teleport to that X/Z, from any dimension to any
    /// dimension, is then applied by the engine at once instead of waiting for the column (the engine
    /// tests the overworld column at the landing X/Z, whatever the target dimension is).
    /// </summary>
    private TextCommandResult OnLoadColumn(TextCommandCallingArgs args)
    {
        int chunkX = (int)args[0] / 32;
        int chunkZ = (int)args[1] / 32;
        _sapi.WorldManager.LoadChunkColumnPriority(
            chunkX,
            chunkZ,
            new ChunkLoadOptions
            {
                KeepLoaded = true,
                OnLoaded = () => _sapi.WorldManager.SaveGame.StoreData(ColumnLoadedKey(chunkX, chunkZ), new byte[] { 1 }),
            });
        return TextCommandResult.Success("loading");
    }

    /// <summary>
    /// A transit with a Yaw to (x, 6, z) of the target followed, in the same tick, by an immediate
    /// <c>TryReturnPlayer</c>: step in and straight back out. Reports "ok:" plus whether the first
    /// hop was still queued (<c>Entity.Teleporting</c>) and whether the return was accepted.
    /// </summary>
    private TextCommandResult OnHopAndReturn(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        try
        {
            _manifold.Transitions.TeleportPlayer(
                player,
                ResolveTargetCode((string)args[1]),
                new TransitionOptions
                {
                    OverridePosition = new BlockPos((int)args[3], FixedSpawn.Y - 2, (int)args[4], 0),
                    Yaw = (float)(double)args[2],
                });
            bool deferred = player.Entity.Teleporting;
            bool returned = _manifold.Transitions.TryReturnPlayer(player);
            return TextCommandResult.Success($"ok:{deferred}:{returned}");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Two transits in the same tick (A, then B: both far off, so both are queued by the engine), then
    /// a third (C) started by a tick listener the moment the engine has applied the first but not the
    /// second: the entity then holds A's coordinates inside B while B's landing is still waiting.
    /// Publishes <see cref="ChainHopKey"/> as "done" when the third transit started in that window, or
    /// "late" when the second had already landed (the scenario fails on that: it proved nothing).
    /// </summary>
    private TextCommandResult OnChainHop(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        var codeB = ResolveTargetCode((string)args[4]);
        var codeC = ResolveTargetCode((string)args[7]);
        double ax = (int)args[2] + 0.5;
        double bx = (int)args[5] + 0.5;
        try
        {
            _manifold.Transitions.TeleportPlayer(
                player,
                ResolveTargetCode((string)args[1]),
                new TransitionOptions { OverridePosition = new BlockPos((int)args[2], FixedSpawn.Y - 2, (int)args[3], 0) });
            _manifold.Transitions.TeleportPlayer(
                player,
                codeB,
                new TransitionOptions { OverridePosition = new BlockPos((int)args[5], FixedSpawn.Y - 2, (int)args[6], 0) });
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

        string key = ChainHopKey((string)args[0]);
        long listener = 0;
        listener = _sapi.Event.RegisterGameTickListener(
            _ =>
            {
                double x = player.Entity.Pos.X;
                bool firstApplied = Math.Abs(x - ax) < 1e-6;
                bool secondApplied = Math.Abs(x - bx) < 1e-6;
                if (!firstApplied && !secondApplied)
                {
                    return;
                }

                _sapi.Event.UnregisterGameTickListener(listener);
                if (secondApplied)
                {
                    _sapi.WorldManager.SaveGame.StoreData(key, "late"u8.ToArray());
                    return;
                }

                _manifold.Transitions.TeleportPlayer(player, codeC, new TransitionOptions { OverridePosition = DefaultLanding((string)args[7]) });
                _sapi.WorldManager.SaveGame.StoreData(key, "done"u8.ToArray());
            },
            1);
        return TextCommandResult.Success("started");
    }

    private TextCommandResult TransitWith(TextCommandCallingArgs args, TransitionOptions options)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        try
        {
            _manifold.Transitions.TeleportPlayer(player, ResolveTargetCode((string)args[1]), options);
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

        return TextCommandResult.Success($"ok:{player.Entity.Teleporting}");
    }

    /// <summary>
    /// Two transits in the same tick: the second starts before the engine applied the first, so the
    /// entity still reports the coordinates it left. Reports "ok:" plus <c>Entity.Teleporting</c> after
    /// the first hop, which a scenario asserts true so it never passes without exercising the deferred case.
    /// </summary>
    private TextCommandResult OnDoubleHop(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        try
        {
            _manifold.Transitions.TeleportPlayer(
                player,
                ResolveTargetCode((string)args[1]),
                new TransitionOptions { OverridePosition = new BlockPos((int)args[2], FixedSpawn.Y - 2, (int)args[3], 0) });
            bool deferred = player.Entity.Teleporting;
            _manifold.Transitions.TeleportPlayer(
                player,
                ResolveTargetCode((string)args[4]),
                new TransitionOptions { OverridePosition = new BlockPos((int)args[5], FixedSpawn.Y - 2, (int)args[6], 0) });
            return TextCommandResult.Success($"ok:{deferred}");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Reports GetOrigin as "code|x|y|z|yaw" (invariant, round-trip precision), or "none".</summary>
    private TextCommandResult OnOrigin(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        TransitOrigin? origin = _manifold.Transitions.GetOrigin(player);
        return TextCommandResult.Success(
            origin is null
                ? "none"
                : string.Create(CultureInfo.InvariantCulture, $"{origin.Dimension.Code}|{origin.X:R}|{origin.Y:R}|{origin.Z:R}|{origin.Yaw:R}"));
    }

    /// <summary>
    /// Drives ITransitionService.TryReturnPlayer and reports "refused", or "returned:" plus whether the
    /// engine is still waiting to apply the move (<c>Entity.Teleporting</c>).
    /// </summary>
    private TextCommandResult OnReturn(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        return TextCommandResult.Success(
            _manifold.Transitions.TryReturnPlayer(player) ? $"returned:{player.Entity.Teleporting}" : "refused");
    }
}
