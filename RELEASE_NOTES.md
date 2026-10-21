# The All You Need Save Manager Continued 1.2.0

Combined player revival, teleport and held-item restoration fixes for PEAK.

- Loading a living snapshot revives a currently dead scout through the game's native network revival before restoring inventory. Scouts saved as dead remain dead; saved unconsciousness and petrification are restored.
- Wait for each player's client to confirm native teleport completion and held-item attachment. Clear full-body motion and equip the saved item once through the native flow.
- Restore saved life state, stamina, status effects and collision again after teleporting, including dead snapshots whose collision the native warp re-enables.
- Match unique saved player names and wait for initialized characters. Report skipped players as partial loads, and report timeouts, disconnects or unsupported clients as failures.
- Retain the 1.1.0 backpack and item-use fixes, original plugin GUID/configuration and format 6 save compatibility.

**Installation:** the host and every restored teammate must install **1.2.0** and fully restart PEAK. Disable the original **The All You Need Save Manager**. The host still controls saving and loading. Keep original saves and save to a new name when checking the update.

**Validation:** Release compilation with zero warnings/errors; 36 item-use regressions; 27 player-load workflow cases; 512 life-state combinations and 32 combined life/warp scenarios, each loaded twice; 15 status replay scenarios; all 101 petrify percentages; save serialization checks; 158 game member and 68 game type references resolved against installed PEAK 2.6 assemblies. Native RPC, private warp cleanup and backpack call-order checks pass. **Unity/Photon gameplay and real host/client tests remain pending.**

Airborne or climbing saves need physical support to stand. Player names must match the save and be unique. The existing save format does not fully record physical thorn/arrow entities, dynamic affliction instances or the report timeline.

Includes a mod ZIP for Thunderstore/mod managers and a separate source ZIP without game DLLs or private data. Original authorship is retained in [NOTICE.md](https://github.com/LeonYew-Ley/PeakSaveManagerContinued/blob/v1.2.0/NOTICE.md).
