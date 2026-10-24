# The All You Need Save Manager Continued 1.2.3

Fix a 1.2.2 regression that left scouts at the segment entrance when a historical save contained luggage that no longer matched the live map. The map jump completed, but the luggage error stopped loading before saved player positions and inventories were restored.

- Wait for luggage objects to become ready, then skip missing, ambiguous or unavailable network objects while restoring all safe matches. Continue to the existing owner-confirmed player position and inventory restoration.
- Keep the type, name, distance and ambiguity checks. A same-named distant box is never used as a replacement. Changed historical map geometry cannot be reconstructed from saved player and prop coordinates.
- Log every skipped saved box and show a yellow partial-load result. Warnings are deduplicated across restoration passes and cleared for the next load. A partial world restore is never reported as complete.
- Retain synchronized open/closed appearance and interaction, late-join synchronization, player revival, held-item restoration, original plugin GUID/configuration and format 6 save compatibility.

**Installation:** every player in the room should install **1.2.3** and fully restart PEAK. Disable the original **The All You Need Save Manager**. The host controls saving and loading. Existing saves need no manual editing.

**Validation:** Release compilation; existing map, warp, item, life-state and player-load regressions; luggage state transitions and matching regressions covering missing first/all boxes, later valid matches, ambiguous objects, unavailable network views, duplicate saved identities and offline matching; native game reference/API and compiled integration checks. **Unity animations, Photon transport and gameplay have not been tested in-game.**

Includes the mod ZIP and a separate source ZIP without game DLLs or private data. Original authorship is retained in [NOTICE.md](NOTICE.md).
