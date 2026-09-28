# Dimensions

A **dimension** in Manifold is a named, isolated world region with its own terrain, player positions, and travel policy. Each dimension is identified by an `AssetLocation` code (e.g., `mymod:nether`) and mapped to a VS engine dimension id (an integer 0-1023).

## Lifecycle

Dimensions follow a well-defined lifecycle:

```
  Define(...).RegisterStatic()/.Create() ──► Active ──► (server shutdown, or removal)
                                                  │
                          restart, owner absent   │  restart, owner loaded but
                          ─────────────────────►  │  not yet re-declared
                                                   ▼
                       Quarantined ◄───────────────  Pending ──► Active
                       (owner re-declares:              (owner re-declares with
                        Pending, then Active)             the same code: Active)
```

A dimension always starts `Active` the moment `RegisterStatic()`/`Create()` completes. What happens
across a restart depends on whether the owning mod is loaded and whether it re-declares the dimension
with the same code - see [Dimension States](#dimension-states) and [Quarantine](#quarantine) below.

### Static vs. Dynamic Registration

| Method | When to use |
|--------|-------------|
| `RegisterStatic()` | Boot-time, from `StartServerSide`. Persistent only (defaults to `Persistent`; `Ephemeral()` is rejected - use `Create()` for that). Call once per boot: on the next server start the same call re-claims the persisted entry under the same internal id, but calling it twice in the same boot throws `DimensionAlreadyRegisteredException`. Use this for dimensions that always exist while your mod is installed. |
| `Create()` | Runtime, after boot. Use for player-created or event-driven dimensions. Requires an explicit `Persistent()` or `Ephemeral()` (throws `DimensionLifetimeUnspecifiedException` otherwise). |

### Persistent vs. Ephemeral

| Lifetime | Chunks persisted | Survives shutdown |
|----------|-----------------|-------------------|
| `Persistent` | Yes | Yes |
| `Ephemeral` | No | No (reaped on transit-out, and at shutdown) |

An `Ephemeral` dimension is reaped automatically when its last occupant **transits out** (`Destroyed` fires and its chunks are discarded), and it is removed at shutdown. **Disconnecting does not reap it** - a logged-out player keeps the dimension and reconnects straight back into it while the server is up. For a dimension a player must be able to leave and return to (including across a restart), use `Persistent`.

A dimension is **never destroyed while a player is inside it**. See [Removing dimensions](#removing-dimensions) below for `TryRemove`, `ForceRemoveDimension`, ephemeral auto-reap, and the admin purge command.

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

**`RegisterStatic()`/`Create()` do not generate any terrain.** Registration only reserves the engine
dimension id and records the dimension in the registry; Manifold's active worldgen driver otherwise
only runs when something visits the dimension (a player transit or a rejoining player). A dimension
registered at boot and never transited into has no chunks until then. To pregenerate it up front -
so it is ready before any player arrives - call `IManifoldServer.GenerateRegion` right after
registering it:

```csharp
manifold.GenerateRegion(dim.Code, new BlockPos(0, 0, 0, 0)); // X/Z only; Y and dimension are ignored
```

This runs the same generation `TeleportPlayer` would (`GenerationRadius` chunks around the given
column), synchronously, on the main thread, with no player involved.

### Reading the Registry

```csharp
// Find by code
IDimension? dim = manifold.Registry.Get(new AssetLocation("mymod", "nether"));

// Iterate all (Active, Pending, Quarantined)
foreach (IDimension d in manifold.Registry.All)
{
    Console.WriteLine($"{d.Code} [{d.State}] owner={d.OwnerModId}");
}

// Which registered dimension is this entity in right now? (overworld for id 0, null if the
// entity's position points at an id nothing has registered)
IDimension? here = manifold.Registry.GetDimensionOf(somePlayer.Entity);

// Who is currently inside a given dimension?
System.Collections.Generic.IReadOnlyList<IServerPlayer> occupants = manifold.GetPlayersIn(dim.Code);
```

### Events

```csharp
manifold.Registry.Created += (_, e) =>
    Mod.Logger.Notification($"Dimension created: {e.Dimension.Code}");

manifold.Registry.Destroyed += (_, e) =>
    Mod.Logger.Notification($"Dimension removed: {e.Dimension.Code}");
```

## Dimension Codes (AssetLocation)

Codes follow the VS `AssetLocation` convention: `domain:path` - e.g., `mymod:nether`. Use your mod's id as the domain to avoid collisions with other mods.

The built-in overworld is `manifold:overworld` (internal id 0). It is a first-class Manifold dimension - readable from the registry, transitable via `ITransitionService.TeleportPlayer`, but immutable (you cannot remove or redefine it).

## Dimension States

| State | Meaning |
|-------|---------|
| `Active` | Normal operation - transit and worldgen permitted. |
| `Pending` | Seen in a prior savegame (or the current one, right after boot) and the owning mod is loaded, but has not (yet, or ever again) re-declared the dimension with `Define(code)...RegisterStatic()`/`Create()` in this session. Transit throws `DimensionStateException` while a dimension stays Pending. |
| `Quarantined` | Owning mod is no longer installed. Chunks are kept on disk, but transit is refused. An admin can release the id with `/manifold purge <code>`. |

## Quarantine

At boot, before any consumer mod's `StartServerSide` runs, Manifold reads the manifest and classifies
each persisted entry right away: `Quarantined` if its owning mod is not in the loaded mod list,
otherwise `Pending`. This prevents id collision and chunk loss: the engine dimension slot and the
saved chunks are preserved either way.

A `Pending` entry becomes `Active` only when its owner calls `Define(code)...RegisterStatic()` (or
`...Create()`) again with the matching code - there is no automatic promotion. If your mod owns a
runtime `Create()`-made `Persistent` dimension, re-declare it at boot with the same code, worldgen
strategy and policies, or it stays `Pending` forever and every transit into it throws
`DimensionStateException`. To find dimensions your mod needs to re-declare, iterate `Registry.All` for
`State == DimensionState.Pending && OwnerModId == yourModId`.

Promotion also keeps the Pending entry's **lifetime**, ignoring `Persistent()`/`Ephemeral()` on the
builder that completes it: a code that was `Persistent` in an earlier session stays `Persistent` even
if you build it with `Ephemeral()` this time (and the reverse). This is deliberate - a Pending entry
already has occupants and saved chunks riding on its original lifetime - but a mismatch is usually a
bug (the wrong builder call, or a code reused for a different dimension), so Manifold logs a warning
naming the code, the requested lifetime, and the one actually kept.

If you reinstall a mod whose dimension was `Quarantined`, that dimension does not become `Active` on
its own either: it becomes `Pending` at the next boot (the owner is loaded again), and then `Active`
once the owner re-declares it, exactly like any other `Pending` entry.

## Removing dimensions

| Method | Scope | Refuses / throws |
|--------|-------|-------------------|
| `IDimensionRegistry.TryRemove(code)` | Any dimension. | Returns `false` if the code is unknown or a connected player is still inside. Throws `DimensionBuiltInImmutableException` for the overworld, `DimensionStateException` for a `Persistent` dimension (use the admin purge command instead). |
| `IManifoldServer.ForceRemoveDimension(code)` | `Ephemeral` only. | Evacuates every connected occupant to the overworld (`LastVisited` position) first, then removes. Returns `false` if the code is unknown, or if an occupant could not be evacuated (the dimension is left in place). For `BuiltIn`/`Persistent` it defers to `TryRemove` - same exceptions, without evacuating anyone first. |
| `/manifold purge <code>` (privilege `controlserver`) | Any non-built-in dimension - `Active`, `Pending`, `Quarantined`, `Persistent` or `Ephemeral`. | Evacuates occupants first; if any player could not be evacuated, reports an error naming how many remain and does not purge. Errors (does not evacuate) if the code is unknown or built-in. On success, releases the engine id and fires `Destroyed`. |

An `Ephemeral` dimension also reaps itself automatically: when its last occupant **transits out** (not
on disconnect), `Destroyed` fires and its chunks are discarded - see [Persistent vs.
Ephemeral](#persistent-vs-ephemeral) above.

Whenever a dimension disappears (any of the above, or a savegame that no longer has it), a player
whose saved position still points at it is not left stranded: `PlayerNowPlaying` checks the joining
player's dimension, and if it is unknown or not `Active`, teleports them to the overworld at their
last-visited position there. This is best-effort and logged, never thrown.

## Dimension Metadata (0.4.0)

`IDimensionBuilder.WithMetadata(string key, object? value)` attaches typed registration-time hints to a dimension. Other systems (a hub UI, another mod, an admin tool) can then query them without going through the owning mod:

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "vault"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .WithMetadata("display_name", "The Vault")
    .WithMetadata("category", "storage")
    .WithMetadata("hub_visible", true)
    .RegisterStatic();

// Anywhere a consumer has an IDimension reference:
string? name = dim.GetMetadata<string>("display_name");
bool visible = dim.GetMetadata<bool>("hub_visible");
if (dim.HasMetadata("category")) { /* ... */ }
```

- Supported value types: primitives, `string`, `enum`, `byte[]`, and `null`. Other types throw `ArgumentException`.
- Setting the same key twice on a builder throws.
- `IDimension.Metadata` is an `IReadOnlyDictionary<string, object?>`; the typed `GetMetadata<T>` extension returns the default value if the key is absent or the stored value is not a `T`.
- Replicated to client mirrors: a connected client's `IManifoldClient.Get(code)!.Metadata` sees the same entries. An enum value is resolved back to its original type by searching the client's loaded assemblies for the owning mod's assembly; if that assembly cannot be found client-side, the value is instead the raw underlying value as a `long`.
- Not persisted across server restarts. For `RegisterStatic` dimensions this is harmless (the owning mod re-declares them on every boot); for runtime `Create` dimensions, treat metadata as ephemeral.

## Per-Dimension Streaming Budget (0.4.0)

`IDimensionBuilder.WithStreamingBudget(int maxColumnsPerTick)` (range 1..64) caps how many columns the streaming driver may ensure for that dimension per tick. Default is 4 - matching the previous global cap.

Budgets are independent across dimensions: a busy dim cannot starve a quiet one, and a background dim can opt into a lower budget so it does not compete with the main world for scheduler slots.

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "arena"))
    .Persistent()
    .WithWorldgen(new ArenaWorldgen())
    .Streaming(loadRadius: 8)
    .WithStreamingBudget(maxColumnsPerTick: 16) // heavy traffic, more slots
    .RegisterStatic();
```

`WithStreamingBudget` is only meaningful in combination with `.Streaming(loadRadius)`.
