namespace AtlasFixture;

using System.Globalization;
using System.IO;
using System.Text;
using Manifold.Api;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
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

    private void RegisterOriginCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create("atlasfx3")
            .WithDescription("Drives Manifold's arrival yaw and return-to-origin surface for Atlas scenarios.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("teleport-player-yaw")
                .WithArgs(parsers.Word("playername"), parsers.Word("dimpath"), parsers.Double("yaw"))
                .HandleWith(OnTeleportPlayerYaw)
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
            w.Write(515.25);
            w.Write(6.0);
            w.Write(509.75);
            w.Write(1.25f);
        }

        _sapi.WorldManager.SaveGame.StoreData(OriginsKey, ms.ToArray());
    }

    /// <summary>Player transit with a Yaw option (radians) and the usual fixed landing.</summary>
    private TextCommandResult OnTeleportPlayerYaw(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        var dimPath = (string)args[1];
        var options = new TransitionOptions { OverridePosition = DefaultLanding(dimPath), Yaw = (float)(double)args[2] };
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

    /// <summary>Drives ITransitionService.TryReturnPlayer and reports its bool result directly.</summary>
    private TextCommandResult OnReturn(TextCommandCallingArgs args)
    {
        IServerPlayer? player = FindPlayer((string)args[0]);
        if (player is null)
        {
            return TextCommandResult.Error($"No online player named {args[0]}.");
        }

        return TextCommandResult.Success(_manifold.Transitions.TryReturnPlayer(player) ? "returned" : "refused");
    }
}
