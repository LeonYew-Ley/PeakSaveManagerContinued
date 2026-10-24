# The All You Need Save Manager Continued 1.2.2

Fix luggage that looks closed but cannot be opened after loading, and synchronize the restored state between the host, teammates and late joiners.

- Restore open/closed state, appearance, interaction availability and attached objects together. Restore respawn-chest skeleton visibility on current PEAK builds.
- Cancel delayed native openings when loading. Closed snapshots clear stale spawn history so they can be opened normally; restoring open snapshots never generates more loot.
- Wait for all current clients to confirm luggage restoration. Check client support before changing world objects and report timeouts, disconnects and world restoration errors as load failures.
- Match same-path boxes using type, name and position. Stop with an explicit error if a saved box has no unique nearby match, rather than modifying another box. Changed historical map geometry cannot be recreated.
- Send fresh live state to late joiners and prevent pending older batches from overriding newer loads.
- Retain the saved-map, player revival, teleport, held-item, backpack and item-use fixes, original plugin GUID/configuration and format 6 save support. Existing saves need no manual editing; older saves use the compatibility fallback for attached-object activation.

**Installation:** every player in the room must install **1.2.2** and fully restart PEAK, including players who are not matched to a saved scout. Disable the original **The All You Need Save Manager**. The host controls saving and loading.

**Validation:** Release compilation; existing map, warp, item, life-state and player-load regressions; luggage state transitions on simulated host/client targets with delayed openings and three repeated restore passes; reopening, identity ambiguity, stale-message ordering, host/scene changes and save compatibility checks; native game reference/API and compiled integration checks. **Actual Unity animations, Photon transport and multiplayer gameplay have not been tested.**

Includes the mod ZIP and a separate source ZIP without game DLLs or private data. Original authorship is retained in [NOTICE.md](NOTICE.md).
