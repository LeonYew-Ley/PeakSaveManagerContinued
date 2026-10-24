# The All You Need Save Manager Continued 1.2.1

Saved-map loading and teleport compatibility fixes for PEAK 3.0.

- Load the resource bundle for the scene recorded in the save before checking availability. Explicit saved scenes never fall back to a different daily map or the active scene.
- Report missing/failed/timed-out map bundles. Record the live biome in new saves and check it before restoring players/world state; legacy volcano codes also reject a temple mismatch.
- Support the PEAK 3.0 status-lock property used by native teleports, with the previous private warp-lock field retained as a fallback.

- Loading a living snapshot revives a currently dead scout through the game's native network revival before restoring inventory. Scouts saved as dead remain dead; saved unconsciousness and petrification are restored.
- Wait for each player's client to confirm native teleport completion and held-item attachment. Clear full-body motion and equip the saved item once through the native flow.
- Restore saved life state, stamina, status effects and collision again after teleporting, including dead snapshots whose collision the native warp re-enables.
- Match unique saved player names and wait for initialized characters. Report skipped players as partial loads, and report timeouts, disconnects or unsupported clients as failures.
- Retain the 1.1.0 backpack and item-use fixes, original plugin GUID/configuration and format 6 save compatibility.

**Installation:** the host and every restored teammate must install **1.2.1** and fully restart PEAK. Disable the original **The All You Need Save Manager**. The host still controls saving and loading. Keep original saves and save to a new name when checking the update.

**Validation:** Release compilation with zero warnings/errors; map identity/preload/failure/biome regressions; current/legacy/unsupported native-lock tests; 36 item-use regressions; 27 player-load workflow cases; 512 life-state combinations and 32 combined life/warp scenarios, each loaded twice; 15 status replay scenarios; all 101 petrify percentages; save serialization checks; 161 game member and 72 game type references resolved against installed PEAK 3.0 assemblies. Native RPC, warp cleanup and backpack call-order checks pass. **Unity/Photon gameplay and real host/client tests remain pending.**

**User validation:** Loading an existing volcano save now enters the volcano again. This confirms the reported map-selection failure is resolved for that save; full player/world restoration and multiplayer acceptance remain unverified.

Same-named maps may change between game versions; this fix cannot recreate removed historical geometry. Old saves lack a live biome name, so only their explicit legacy volcano code is checked. Airborne or climbing saves need physical support to stand. Player names must match the save and be unique. The existing save format does not fully record physical thorn/arrow entities, dynamic affliction instances, lava progress or the report timeline.

Includes a mod ZIP for manual installation/mod managers and a separate source ZIP without game DLLs or private data. Original authorship is retained in [NOTICE.md](NOTICE.md).
