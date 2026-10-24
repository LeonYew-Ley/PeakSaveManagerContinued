## 1.2.3 — Continue player restoration when historical luggage is missing

- Fix the 1.2.2 regression where a missing or ambiguous saved luggage object aborted loading after switching segments, leaving scouts at the segment entrance before their saved positions and inventories were restored.
- Wait for luggage objects to become ready, then skip unmatched objects and continue restoring every safe match and the saved players. Keep the type, name, distance and ambiguity checks; do not guess a distant replacement.
- Report skipped luggage in the log and show a yellow partial-load result instead of claiming complete world restoration. Clear and deduplicate warnings for each load.
- Retain synchronized luggage state, late-join handling, player-owner confirmation and format 6 compatibility. Every room participant should update to 1.2.3 and restart PEAK.
- Add regressions for missing first/all boxes, ambiguous matches, unavailable network views, offline matching and duplicate saved identities. Managed/IL checks do not replace in-game testing.

## 1.2.2 — Luggage restoration and multiplayer synchronization

- Restore luggage appearance, interaction state and the native available-luggage list together on the host and all current clients. Fix closed-looking boxes that could not be opened after loading.
- Restore closed snapshots in multiplayer, cancel pending native opening routines and reset stale loot history so the box can be opened normally again. Loading an open box never regenerates loot.
- Synchronize attached objects and respawn-chest skeleton state. Late joiners receive the current live state, with older pending messages prevented from overriding newer loads.
- Match same-path boxes by prefab type, name and nearby position. Missing or ambiguous boxes report a load failure instead of applying state to an arbitrary distant box.
- Require every player in the room to use 1.2.2, check support before restoring world objects and wait for client acknowledgements. World restoration errors now reach the load failure handler.
- Keep format 6 saves compatible; new saves retain attached-object activation explicitly. Add luggage behavior, identity, message-order and game API checks. Unity/Photon gameplay remains unverified.

## 1.2.1 — Saved maps and PEAK 3.0 compatibility

- Preload the resource bundle for the saved scene before checking scene availability. Explicit saved map names/numbers never fall back to a remapped daily index or the current scene.
- Report missing, failed or timed-out map resources instead of silently choosing another map.
- Capture the live saved biome and check it before restoring players/world state; legacy volcano codes also reject a volcano-to-temple mismatch. Same-named maps can still change between game versions.
- Support the PEAK 3.0 teleport status-lock property while retaining the previous private field fallback.
- Require the updated player-owner protocol so old clients cannot acknowledge this build's restoration requests.
- Add map-loading and native-lock regressions. Managed/IL verification does not replace in-game host/client testing.
- User confirmed that loading an existing volcano save enters the volcano again. Full multiplayer restoration remains unverified.

## 1.2.0 — Player load restoration

- Restore held items once through the character owner's native equip flow, including empty hands and legacy slot fallbacks, without assigning item templates to runtime state.
- Wait for owner-confirmed teleport completion and item attachment; report timeouts, missing owner support and disconnects as failures.
- Resume at rest with full-body motion cleared, keeping saved positions, view, inventory and existing save format.
- Restore saved living scouts through native network revival before inventory, without extra revival penalties; preserve saved dead/unconscious states.
- Match unique saved player names, wait for initialized characters and report skipped players as partial loads.
- Finalize saved life state, stamina and collision on the player's owner after teleport completion; keep dead saved scouts from regaining body collision during the native warp.
- Require the host and every restored teammate to install the same version; the host still controls loading.
- Add managed selection, ordering and failure regressions. Unity/Photon gameplay validation remains pending.

## 1.1.0 — Continued

- Fixed worn backpacks disappearing during inventory synchronization after loading a save.
- Report player restore failures instead of continuing to a successful load message.
- Restore ItemUses with the optional integer type expected by PEAK, preserving generic entries and unlimited-use state for inventory and ground items.
- Normalize wrongly typed integer use counts saved after an older restore.
- Preserve the original plugin GUID, configuration and save format for migration.
- Add managed restore regression checks and game assembly compatibility verification.
- Publish the community continuation under LeonYew, with original author attribution.

## 1.0.9

- Fixed saving on current PEAK builds after the run timer field became private.
- Updated backpack capture and restore for the current game API, including backpack type.
- Fixed the plugin's embedded version number to match the release version.

## 1.0.8

- Fixed multiplayer save loads stopping early when the full saved party was not matched before restore.
- Multiplayer loads now wait briefly for joiners, then continue with every matching player instead of cancelling the whole load.
- Added the source project and verification tool to the GitHub repo so contributors can build and submit fixes more easily.

## 1.0.7

- Fixed multiplayer load continuing with only the host matched instead of waiting for the full saved party.
- Improved multiplayer player matching during load from Airport into a saved run.
- Added clearer load failure messaging when saved players are missing or not ready yet.

## 1.0.6

- Fixed duplicate item data key restore failures during load, including the reported `CookedAmount` crash.
- Prevented ground item restore errors from blocking the player position restore that happens later in the load flow.
- Improved reliability when loading host saves back from Airport into multiplayer runs.

## 1.0.5

- Fixed a load pipeline failure that could stop restore early during scout or other world object restore work.
- Prevented scout cannon restore mismatches from aborting the rest of the load.
- Improved load failure logging and UI error reporting.
- Added scout coverage to save verification tests.

## 1.0.4

- Fixed save creation failing on some installs when the game root save folder was not writable.
- Added automatic fallback to a writable save folder.
- Improved save failure messages so the UI shows the actual reason when possible.

## 1.0.3

- Added GitHub project link in `manifest.json`.
- Refreshed README content and added icon preview.
- Cleaned package metadata for release page quality.

## 1.0.2

- Packaged release as TAYNSM.dll.
- Clean Thunderstore export layout.
- Documented known bug for Shelf Fungus and Bounce Fungus restore behavior.
