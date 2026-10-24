# The All You Need Save Manager Continued 1.2.4

Restore grounded saves safely when their original coordinates no longer have stable support in the current map.

- Preserve supported saved positions; otherwise search nearby static ground within 8m horizontally / 8m vertically and 10m total. Validate slope, nine support samples and body clearance. Report displacement without modifying the save file.
- Require grounded players to settle after owner teleport and equipment restoration; fail explicitly on unsafe/missing landings. Keep original restoration behavior for airborne, climbing, dead and unconscious saves.
- Share one 5-second missing-luggage readiness budget across restoration passes.
- All room participants need 1.2.4 and must restart PEAK. Managed/API checks passed; actual Unity/Photon gameplay remains unverified.

Disable the original The All You Need Save Manager. Existing format 6 saves and user configuration remain compatible. Saved coordinates cannot reconstruct historical map geometry. Original authorship is retained in [NOTICE.md](NOTICE.md).
