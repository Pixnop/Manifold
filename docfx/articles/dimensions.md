# Dimensions

A **dimension** in Manifold is a named, isolated world region with its own terrain, player positions, and travel policy. Each dimension is identified by an `AssetLocation` code (e.g., `mymod:nether`) and mapped to a VS engine dimension id (an integer 0-1023).

## Lifecycle

Dimensions follow a well-defined lifecycle:

```
  Define (boot)  ──►  Active  ──►  (server shutdown)
  Create (runtime)              └──►  Quarantined  (owning mod removed)
```

### Static vs. Dynamic Registration

| Method | When to use |
|--------|-------------|
| `RegisterStatic()` | Boot-time, from `StartServerSide`. Idempotent - re-calling on a subsequent server start reuses the same internal id. Use this for dimensions that always exist while your mod is installed. |
| `Create()` | Runtime, after boot. Use for player-created or event-driven dimensions. Lifetime must be `Ephemeral` or `Persistent`. |

### Persistent vs. Ephemeral

| Lifetime | Chunks persisted | Survives shutdown |
|----------|-----------------|-------------------|
| `Persistent` | Yes | Yes |
| `Ephemeral` | No | No (removed on shutdown) |

Ephemeral dimensions can be removed at runtime via `IDimensionRegistry.TryRemove(code)`. Persistent dimensions cannot be removed through the API (use `/manifold purge <code>` as an admin to release quarantined ones).

## The Registry

`IManifoldServer.Registry` is the server-side source of truth. Obtain the **owner-scoped** facade with `sapi.GetManifoldServer(this)` - this is required when calling `Registry.Define` so that your mod id is recorded as the dimension owner.

```csharp
var manifold = sapi.GetManifoldServer(this);  // 'this' is your ModSystem

IDimension dim = manifold.Registry
    .Define(new AssetLocation("mymod", "nether"))
    .Persistent()
    .WithWorldgen(new MyNetherWorldgenStrategy())
    .RegisterStatic();
```

### Reading the Registry

```csharp
// Find by code
IDimension? dim = manifold.Registry.Get(new AssetLocation("mymod", "nether"));

// Iterate all (Active, Pending, Quarantined)
foreach (IDimension d in manifold.Registry.All)
{
    Console.WriteLine($"{d.Code} [{d.State}] owner={d.OwnerModId}");
}
```

### Events

```csharp
manifold.Registry.Created += (_, e) =>
    Mod.Logger.Notification($"Dimension created: {e.Dimension.Code}");

manifold.Registry.Destroyed += (_, e) =>
    Mod.Logger.Notification($"Dimension removed: {e.DimensionCode}");
```

## Dimension Codes (AssetLocation)

Codes follow the VS `AssetLocation` convention: `domain:path` - e.g., `mymod:nether`. Use your mod's id as the domain to avoid collisions with other mods.

The built-in overworld is `manifold:overworld` (internal id 0). It is a first-class Manifold dimension - readable from the registry, transitable via `ITransitionService.TeleportPlayer`, but immutable (you cannot remove or redefine it).

## Dimension States

| State | Meaning |
|-------|---------|
| `Active` | Normal operation - transit and worldgen permitted. |
| `Pending` | Seen in a prior savegame but the owning mod has not yet re-registered it in this session. Typically resolves to Active within the same boot once the mod's `StartServerSide` runs. |
| `Quarantined` | Owning mod is no longer installed. Chunks are kept on disk, but transit is refused. An admin can release the id with `/manifold purge <code>`. |

## Quarantine

When a savegame is loaded and Manifold finds a persisted dimension whose owning mod is absent from the loaded mod list, the dimension enters the `Quarantined` state. This prevents id collision and chunk loss: the engine dimension slot and the saved chunks are preserved until an admin explicitly purges the entry.

If you reinstall the owning mod, the dimension automatically transitions from `Pending` back to `Active` at the next server start.
