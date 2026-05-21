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

- [ ] **Boot clean** - Server log includes `[Manifold] Ready - 2 dimensions known (2 active, 0 quarantined)` (overworld + manifoldsample:void). No exceptions in the boot log.
- [ ] **Sample registered** - Log includes `[ManifoldSample] Registered void dimension and /voiddim command.`
- [ ] **Command transit** - In-game, run `/voiddim`. The player teleports to a flat void world. The chat displays "Teleported to manifoldsample:void."
- [ ] **Block transit** - Open creative inventory, give yourself a `voidportal` block, place it, step on it. Player teleports.
- [ ] **Return** - From the void dim, `/tp 0 100 0` (or any spawn-adjacent overworld coords) brings the player back. Verify the player is in dim 0 (the overworld).
- [ ] **Save and quit, restart** - Server log on second boot still shows `2 dimensions known (2 active, 0 quarantined)`. `/voiddim` still works. The manifoldsample:void chunks where you walked are preserved (visit the spot you were last at).
- [ ] **Uninstall sample** - Remove the `ManifoldSample/` folder. Restart. Server log shows `[Manifold] Ready - 2 dimensions known (1 active, 1 quarantined)` AND a warning line for the manifoldsample:void dimension's owner not being loaded.
- [ ] **Re-install sample** - Drop the `ManifoldSample/` folder back. Restart. Log shows the dim promoted from Pending → Active and the SAME internal id (typically 10).

## Failure triage

If any step fails, the failing assertion identifies the layer:

| Failing step | Layer at fault |
|---|---|
| Boot clean | ManifoldModSystem wiring, HarmonyPatcher, or VS API surface drift |
| Sample registered | GetManifoldServer extension, DimensionBuilder, BasicVoidWorldgenStrategy |
| Command transit | DimensionCommandBuilder, TransitService, PlayerTeleporter (cross-dim) |
| Block transit | PortalBlockBase.OnEntityCollide, ManifoldAccess resolver |
| Return | TransitService source-side detection |
| Save / restart | DimensionPersistence, manifest format |
| Quarantine on uninstall | DimensionPersistence.Classify + Registry.SeedFromManifest |
| Promotion on re-install | Registry.Complete promotion path |
