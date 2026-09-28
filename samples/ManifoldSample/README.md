# ManifoldSample - smoke test harness

This mod is the **manual smoke harness** for Manifold. It must be run inside a Vintage Story dev instance after every meaningful change to Manifold and **before every release**.

## Setup

1. Build everything from the repo root:
   ```
   dotnet build -c Release
   ```

2. Locate the build outputs:
   - `src/Manifold/bin/Release/net10.0/Manifold.dll` + `src/Manifold/modinfo.json`
   - `samples/ManifoldSample/bin/Release/net10.0/ManifoldSample.dll` + `samples/ManifoldSample/modinfo.json` + `samples/ManifoldSample/assets/`

3. Create two folders in `%appdata%/VintagestoryData/Mods/`:
   - `Manifold/` containing `Manifold.dll` + `modinfo.json`
   - `ManifoldSample/` containing `ManifoldSample.dll` + `modinfo.json` + `assets/`

4. Launch Vintage Story in singleplayer.

## Smoke checklist

Run through every item before tagging a release.

- [ ] **Boot clean** - Server log includes `[Manifold] Ready - 6 dimensions known (6 active, 0 quarantined)` (overworld + manifoldsample:void/flat/dark/stream/vault). No exceptions in the boot log.
- [ ] **Sample registered** - Log includes `[ManifoldSample] Registered void + flat + dark + stream + vault dimensions and /voiddim, /flatdim, /darkdim, /streamdim, /vaultdim, /overworlddim, /sendtestitem, /sendtestblock, /createtempdim, /destroytempdim, /miningdim, /miningreset commands.`
- [ ] **Command transit** - In-game, run `/voiddim`. The player teleports to a flat void world. The chat displays "Teleported to manifoldsample:void."
- [ ] **Block transit** - Open creative inventory, give yourself a `voidportal` block, place it, step on it. Player teleports.
- [ ] **Return** - `/overworlddim` returns the player to their last-visited overworld position. Verify the player is in dim 0 (the overworld).
- [ ] **Dark sky** - Run `/darkdim`. The dimension is dark except for block light; place a torch to confirm it lights the area.
- [ ] **Streaming** - Run `/streamdim` and walk in a straight line; new chunks generate ahead of you with no visible pause.
- [ ] **Separate inventory** - Run `/vaultdim`. Your hotbar/backpack/character inventory is swapped out (empty on first visit); `/overworlddim` back and confirm your overworld items are intact.
- [ ] **Entity and block transit demos** - `/sendtestitem` spawns a stick and sends it to the flat dimension (find it near your X/Z in `/flatdim`). Place a chest with items, look at it, run `/sendtestblock`, and confirm the chest and its contents arrived in the flat dimension.
- [ ] **Ephemeral lifecycle** - `/createtempdim` then `/overworlddim` (leave normally): the dimension auto-reaps once empty. `/createtempdim` again, then `/destroytempdim` while still inside: you are evacuated and the dimension is force-removed immediately.
- [ ] **Mining dimension** - `/miningdim`: creates manifoldsample:mining and teleports you in ("Created manifoldsample:mining and sent you in."). You land in a lit air room, not inside rock; dig outward to find ore blocks. `/miningdim` again: no re-creation, just a teleport ("Sent you to manifoldsample:mining."). `/miningreset` (needs the `controlserver` privilege): evacuates you to the overworld and removes the dimension ("Reset manifoldsample:mining; run /miningdim to generate a fresh one."). Running `/miningreset` again with nothing registered replies "Nothing to reset - manifoldsample:mining is not currently registered." `/miningdim` after a reset regenerates the dimension with a different ore layout (same rock shell and spawn room).
- [ ] **Save and quit, restart** - Server log on second boot still shows `6 dimensions known (6 active, 0 quarantined)`. `/voiddim` still works. The manifoldsample:void chunks where you walked are preserved (visit the spot you were last at).
- [ ] **Uninstall sample** - Remove the `ManifoldSample/` folder. Restart. Server log shows `[Manifold] Ready - 6 dimensions known (1 active, 5 quarantined)`.
- [ ] **Re-install sample** - Drop the `ManifoldSample/` folder back. Restart. `/voiddim` (and the other sample commands) work again with the SAME internal ids as before uninstalling; Manifold does not log a promotion line, so verify by transit rather than by log message.

## Failure triage

If any step fails, the failing assertion identifies the layer:

| Failing step | Layer at fault |
|---|---|
| Boot clean | ManifoldModSystem wiring, or VS API surface drift |
| Sample registered | GetManifoldServer extension, DimensionBuilder, BasicVoidWorldgenStrategy |
| Command transit | DimensionCommandBuilder, TransitService, PlayerTeleporter (cross-dim) |
| Block transit | PortalBlockBase.OnEntityCollide, ManifoldAccess resolver |
| Return | TransitService source-side detection |
| Dark sky | DimensionGenerator.PlaceSkyCapIfConfigured, cap block resolution |
| Streaming | StreamingWorldgenDriver, DimensionGenerator.EnsureColumn |
| Separate inventory | InventoryProfileResolver, TransitService.ApplyInventoryPolicy |
| Entity / block transit demos | TransitService.TeleportEntity / TeleportBlock |
| Ephemeral lifecycle | TransitService reap-on-leave, ManifoldServerFacade.ForceRemoveDimension |
| Mining dimension | MiningWorldgenStrategy, ManifoldServerFacade.ForceRemoveDimension |
| Save / restart | DimensionPersistence, manifest format |
| Quarantine on uninstall | DimensionPersistence.Classify + Registry.SeedFromManifest |
| Re-install | Registry.DefineForOwner re-claim path |
