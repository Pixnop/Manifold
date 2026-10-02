using System;
using Manifold.Api.Client;
using Manifold.Api.Events;
using Manifold.Api.Transitions;
using Xunit;

namespace Manifold.Pure.Tests.ApiSurface;

/// <summary>
/// Members that mods already compiled against an older Manifold call by signature. Removing one
/// compiles fine here but makes those mods throw <see cref="MissingMethodException"/> at runtime.
/// </summary>
public sealed class BinaryCompatibilityTests
{
    [Fact]
    public void TransitionOptions_Should_Declare_A_Parameterless_Constructor()
    {
        // A struct only gets a .ctor() in metadata when it declares one; `new TransitionOptions { ... }`
        // in a mod built against 0.4.1 calls it.
        Assert.NotNull(typeof(TransitionOptions).GetConstructor(Type.EmptyTypes));

        var options = new TransitionOptions();
        Assert.Null(options.SpawnBehavior);
    }

    [Fact]
    public void TransitionOptions_Should_Keep_PreserveInventory_Settable()
    {
        var property = typeof(TransitionOptions).GetProperty("PreserveInventory");

        Assert.NotNull(property?.GetMethod);
        Assert.NotNull(property?.SetMethod);

#pragma warning disable CS0618 // the obsolete member is what old mods call
        var options = new TransitionOptions { PreserveInventory = true };
        Assert.True(options.PreserveInventory);
#pragma warning restore CS0618
    }

    [Fact]
    public void IManifoldClient_Should_Keep_LocalPlayerTransited_Subscribable()
    {
        // Mods built against pre-0.6 Manifold reference this event by name; obsoleting it must not
        // remove it (that would throw MissingMemberException for them at load time).
        var eventInfo = typeof(IManifoldClient).GetEvent("LocalPlayerTransited");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<PlayerEnteredDimensionEventArgs>), eventInfo!.EventHandlerType);
    }

    [Fact]
    public void PlayerEnteredDimensionEventArgs_Should_Keep_Its_Four_Argument_Constructor()
    {
        // Mods built before 0.6.1 construct it with these four parameters; 0.6.1 adds an overload
        // with a yaw, it must not replace this one.
        var ctor = typeof(PlayerEnteredDimensionEventArgs).GetConstructor(
        [
            typeof(Vintagestory.API.Server.IServerPlayer),
            typeof(Manifold.Api.IDimension),
            typeof(Manifold.Api.IDimension),
            typeof(Vintagestory.API.MathTools.BlockPos),
        ]);

        Assert.NotNull(ctor);
    }

    [Fact]
    public void TransitionOptions_Should_Have_No_Yaw_By_Default()
    {
        Assert.Null(new TransitionOptions().Yaw);
        Assert.Equal(1f, new TransitionOptions { Yaw = 1f }.Yaw);
    }

    [Fact]
    public void IDimensionBuilder_Should_Only_Have_Gained_Members_Since_0_6_0()
    {
        // Mods built against 0.4.1..0.6.0 call these through the interface; 0.6.1 adds
        // WithRespawnBehavior next to them and must not have moved or removed any.
        var builder = typeof(Manifold.Api.Server.IDimensionBuilder);
        string[] existing =
        [
            "WithWorldgen", "Persistent", "Ephemeral", "WithGenerationRadius", "WithRelightHeight", "WithSpawnBehavior",
            "WithFixedSpawn", "WithForcedGameMode", "Streaming", "WithStreamingBudget", "WithDarkSky",
            "WithSeparateInventory", "WithMetadata", "RegisterStatic", "Create",
        ];

        Assert.All(existing, name => Assert.NotNull(builder.GetMethod(name)));
        Assert.NotNull(builder.GetMethod("WithRespawnBehavior", [typeof(RespawnBehavior)]));
    }

    [Fact]
    public void RespawnBehavior_Should_Default_To_The_Overworld()
    {
        // A dimension registered without the option is the zero value: a mod built before the option
        // existed gets the safe behavior (nobody respawns stuck in the dimension).
        Assert.Equal(0, (int)RespawnBehavior.Overworld);
        Assert.Equal(RespawnBehavior.Overworld, default);
    }
}
