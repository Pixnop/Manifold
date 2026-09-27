using System;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Manifold.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Manifold.Api.Helpers;

/// <summary>
/// Fluent builder for a chat command that teleports the calling player to a configured dimension.
/// </summary>
/// <remarks>Server-side. Call <see cref="Register"/> from <c>StartServerSide</c>.</remarks>
public sealed class DimensionCommandBuilder
{
    /// <summary>Command name (without slash). Set via <see cref="Command"/>.</summary>
    public string? Name { get; private set; }

    /// <summary>Target dimension code. Set via <see cref="TargetDimension"/>.</summary>
    public AssetLocation? Target { get; private set; }

    /// <summary>Required privilege. Defaults to <c>"chat"</c>.</summary>
    public string Privilege { get; private set; } = "chat";

    /// <summary>Human-readable description shown in <c>/help</c>.</summary>
    public string Description { get; private set; } = "Teleport to a custom dimension.";

    /// <summary>Transit options. Defaults to <see cref="TransitionOptions"/> defaults.</summary>
    public TransitionOptions Options { get; private set; }

    /// <summary>Set the command name (without leading slash).</summary>
    /// <param name="name">Command name.</param>
    /// <returns>This builder.</returns>
    public DimensionCommandBuilder Command(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>Set the target dimension code.</summary>
    /// <param name="target">Asset code.</param>
    /// <returns>This builder.</returns>
    public DimensionCommandBuilder TargetDimension(AssetLocation target)
    {
        Target = target;
        return this;
    }

    /// <summary>Override the default privilege.</summary>
    /// <param name="privilege">Privilege name.</param>
    /// <returns>This builder.</returns>
    public DimensionCommandBuilder RequiresPrivilege(string privilege)
    {
        Privilege = privilege;
        return this;
    }

    /// <summary>Override the default description.</summary>
    /// <param name="description">New description.</param>
    /// <returns>This builder.</returns>
    public DimensionCommandBuilder DescribedAs(string description)
    {
        Description = description;
        return this;
    }

    /// <summary>Override the default transit options.</summary>
    /// <param name="options">New options.</param>
    /// <returns>This builder.</returns>
    public DimensionCommandBuilder WithOptions(TransitionOptions options)
    {
        Options = options;
        return this;
    }

    /// <summary>Sets a per-transit spawn behavior override (e.g. LastVisited when returning to the overworld).</summary>
    /// <param name="behavior">The spawn behavior to apply for this command's transits.</param>
    /// <returns>This builder.</returns>
    public DimensionCommandBuilder WithSpawnBehavior(SpawnBehavior behavior)
    {
        Options = Options with { SpawnBehavior = behavior };
        return this;
    }

    /// <summary>Throw if the builder is incomplete. Public for testability.</summary>
    /// <exception cref="InvalidOperationException">Required field missing.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("DimensionCommandBuilder: Command(name) is required.");
        }

        if (Target is null)
        {
            throw new InvalidOperationException("DimensionCommandBuilder: TargetDimension(code) is required.");
        }
    }

    /// <summary>Register the command with VS chat commands.</summary>
    /// <param name="sapi">Server API.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sapi"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Command"/> or <see cref="TargetDimension"/> was not set (see <see cref="Validate"/>).
    /// </exception>
    public void Register(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        Validate();
        sapi.ChatCommands
            .Create(Name!)
            .WithDescription(Description)
            .RequiresPrivilege(Privilege)
            .RequiresPlayer()
            .HandleWith(args =>
            {
                if (args.Caller.Player is not IServerPlayer player)
                {
                    return TextCommandResult.Error("Players only.");
                }

                var manifold = ManifoldAccess.GetServer(sapi);
                if (manifold is null)
                {
                    return TextCommandResult.Error("Manifold not loaded.");
                }

                if (!manifold.IsHealthy)
                {
                    return TextCommandResult.Error("Manifold is unhealthy; transit unavailable.");
                }

                return TryTeleport(manifold, player, Target!, Options);
            });
    }

    /// <summary>
    /// Attempts the transit and turns the outcome into a chat reply: an error naming the failure when
    /// the target is missing or not Active (<see cref="Manifold.Api.ManifoldException"/>), an error
    /// when a <c>PlayerEntering</c>/<c>PlayerArriving</c> subscriber cancels the transit, success
    /// otherwise. Detects the cancelled case via <see cref="TransitService.TryTeleportPlayer"/> when
    /// <paramref name="manifold"/>'s <c>Transitions</c> is that internal type (always true outside of
    /// tests) - <see cref="Server.ITransitionService.TeleportPlayer"/> itself stays <c>void</c>.
    /// </summary>
    /// <param name="manifold">Manifold's server facade.</param>
    /// <param name="player">The player to teleport.</param>
    /// <param name="target">Target dimension code.</param>
    /// <param name="options">Transit options.</param>
    /// <returns>A success reply if the player moved; an error reply otherwise.</returns>
    internal static TextCommandResult TryTeleport(
        IManifoldServer manifold, IServerPlayer player, AssetLocation target, TransitionOptions options)
    {
        try
        {
            bool moved = manifold.Transitions is TransitService core
                ? core.TryTeleportPlayer(player, target, options)
                : TeleportAndAssumeMoved(manifold.Transitions, player, target, options);
            return moved
                ? TextCommandResult.Success($"Teleported to {target}.")
                : TextCommandResult.Error("Transit was cancelled.");
        }
        catch (ManifoldException ex)
        {
            return TextCommandResult.Error(ex.Message);
        }
    }

    private static bool TeleportAndAssumeMoved(
        ITransitionService transitions, IServerPlayer player, AssetLocation target, TransitionOptions options)
    {
        transitions.TeleportPlayer(player, target, options);
        return true;
    }
}
