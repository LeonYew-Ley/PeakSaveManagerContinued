# The All You Need Save Manager Continued

![Continued icon](https://github.com/LeonYew-Ley/PeakSaveManagerContinued/raw/main/icon.png)

All-in-one save manager for PEAK. Save a run, load it later, and restore the run setup, players, inventory, and world state as closely as the game allows.

An independent community continuation of **llucasandersen's The All You Need Save Manager**, maintained by **LeonYew / LeonYew-Ley**. Original source and attribution are retained in [NOTICE.md](NOTICE.md).

## Continued 1.1.0

- Keeps worn backpacks and their saved contents equipped after inventory synchronization.
- Fixes restored item-use data that caused `InvalidCastException` with current PEAK.
- Preserves complete item data, absent/unlimited use counts, existing saves and configuration.
- Reports player restoration failures through the load error handler.

## What It Syncs

### Run setup
- Scene name (`Level_X`)
- Level id and segment
- Seed and ascent
- Run day, run timer, and time of day

### Player state
- Position and rotation
- Velocity and angular velocity
- Stamina and extra stamina
- Status effects
- Checkpoint ownership links

### Inventory
- Main slots
- Backpack slots
- Temp slot
- Equipped and selected slot
- Held item id when available
- Item uses and item data entries

### World and interactables
- Campfire state and progress
- Luggage and cursed luggage state
- Respawn chest and mirage luggage state
- Ground items with transform and rigidbody data

### Deployables and world objects
- Piton and piton handle
- Rope anchor, rope, rope anchor with rope, rope anchor projectile
- Chain launcher
- Magic bean vine
- Cloud fungus
- Bounce fungus
- Shelf fungus
- Portable stove
- Checkpoint flag and checkpoint constructable
- Scout cannon

## How To Use

1. Open the pause menu and click `SAVE MANAGER`.
2. Click `Quick Save`, or enter a save name and click `Save`.
3. In the save list, click `Load` on the save you want.
4. Use `Overwrite` to replace an existing save with your current run state.

## Autosave

Autosave can be disabled in the BepInEx config file.

- Config file: `BepInEx/config/com.lucasandersen.peakallyouneedsavemanager.cfg`
- Section: `AutoSave`
- Setting: `Enable AutoSave = false`
- Timer setting: `Interval Seconds = 300`

If the config file does not exist yet, launch the game once with the mod installed and BepInEx will generate it.

## Loading Notes

- You can load from Airport.
- You can load while already in a run.
- In multiplayer, the host controls save and load.
- Multiplayer loads now restore every saved player that matches, even if the full saved party is not present yet.

## Save Files

- Folder: `<PEAK root>/PeakSaves`
- Extension: `.peaksave.json`
- Versioned format with compatibility checks for safe loading
- If the game install folder is not writable, TAYNSM falls back to a writable save folder automatically

## Installation

1. Disable or uninstall the original **The All You Need Save Manager** before installing this continuation.
2. Install this package and BepInEx through your mod manager.
3. Fully restart PEAK.

For manual installation, extract so this path exists:

`BepInEx/plugins/PeakSaveManagerContinued/PeakSaveManagerContinued.dll`

The original plugin GUID and configuration filename are retained. Load an existing complete save and save to a new name when checking the update. Items already missing from a saved file cannot be reconstructed by this fix.

## Source And Contributing

The GitHub repo now includes the source project and the verification tool.

- Source project: `src/PeakSaveManager`
- Verification tools: `tools/SaveVerificationTests`, `tools/Verify-ItemUsageData.ps1`, `tools/Verify-Compatibility.ps1`
- Issues: https://github.com/LeonYew-Ley/PeakSaveManagerContinued/issues
- Pull requests: https://github.com/LeonYew-Ley/PeakSaveManagerContinued/pulls
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)

Build command:

```powershell
./Build.ps1 -GameManagedPath '<PEAK>/PEAK_Data/Managed' -BepInExCorePath '<profile>/BepInEx/core'
```

This builds `build/PeakSaveManagerContinued.dll` and runs the managed verification checks. Requires PowerShell 7, a .NET SDK with .NET 9 runtime support, and local PEAK/BepInEx assemblies. Third-party assemblies are not included.

Package the mod and a separate source archive:

```powershell
./scripts/package.ps1
```

## Changelog

### 1.1.0
- Community continuation with backpack and item data restoration repairs.
- Added 36 managed item-restore regression cases and game assembly compatibility checks.

Older release notes are in [CHANGELOG.md](CHANGELOG.md).

## Known Bugs

- Shelf fungus can fail to restore in some saves.
- Bounce fungus can fail to restore in some saves.

## Validation

Release compilation, 36 managed item-restore cases and the existing save serialization checks pass against installed PEAK 2.6 assemblies. Compatibility verification resolves 121 game member references and 65 game type references and checks backpack restoration call order. In-game singleplayer and host/client testing remains pending; the automated checks do not launch Unity or Photon.

## Links

- GitHub: https://github.com/LeonYew-Ley/PeakSaveManagerContinued
- Thunderstore: https://thunderstore.io/c/peak/p/LeonYew/The_All_You_Need_Save_Manager_Continued/
- Original project: https://github.com/llucasandersen/The_All_You_Need_Save_Manager
