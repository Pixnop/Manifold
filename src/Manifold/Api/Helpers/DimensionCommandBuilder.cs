using System;
using Manifold.Api.Transitions;
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
    public void Register(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        Validate();
        sapi.ChatCommands
            .Create(Name!)
            .WithDescription(Description)
            .RequiresPrivilege(Privilege)
            .HandleWith(args =>
            {
                if (args.Caller.Player is not IServerPlayer player)
                {
                    return TextCommandResult.Error("This command is server-only.");
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

                manifold.Transitions.TeleportPlayer(player, Target!, Options);
                return TextCommandResult.Success($"Teleported to {Target}.");
            });
    }
}
