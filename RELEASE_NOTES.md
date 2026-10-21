# The All You Need Save Manager Continued 1.1.0

Independent maintenance release of llucasandersen's PEAK save manager.

- Restore the worn backpack prefab before inventory synchronization, keeping the pack and its saved contents equipped.
- Restore item use counts with the optional integer data expected by current PEAK. Preserve absent/unlimited state and repair older integer entries.
- Report player restoration failures through the load error handler.
- Keep existing saves, settings and the original plugin GUID compatible.

Disable the original **The All You Need Save Manager** before installing this
package. Keep original saves and test by saving to a new name after loading.

Validation: Release compilation with zero warnings/errors; 36 managed item
restoration regression cases; existing save serialization checks; 121 game member
references and 65 game type references resolved, plus backpack call-order and
typed-restore compatibility checks. These checks use the installed PEAK 2.6
assemblies. In-game host/client verification remains pending.

The release includes a mod ZIP for Thunderstore/mod managers and a separate
source ZIP without game DLLs or private data. See NOTICE.md for attribution.
