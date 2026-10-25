# The All You Need Save Manager Continued 1.2.5

Fix teammates failing to follow the host into an older saved map when its resource bundle was not loaded on their machine.

- Check support and prepare the exact saved map on all participants before switching scenes. Arm the saved seed/ascent only after all resources are ready.
- Wait for every participant's destination scene and initialized character before restoring players, inventory or world state.
- Stop on preparation errors, timeouts and party changes. Cancel pending preparation and seed overrides; guard the native loading process against a cancelled late switch. Never use local-only scene fallback in an online room.
- All participants need 1.2.5 and must restart PEAK. An already-started native scene switch cannot be rolled back automatically.

Validation: the user confirmed two-player Unity/Photon acceptance passed on the tested build and authorized release. The release build, managed regressions, game API checks and compiled gate-order checks also pass. Failure scenarios covered by managed tests have not all been separately confirmed in game.

Existing format 6 saves and user configuration remain compatible. Retain the stable-landing and luggage fixes from 1.2.4. Original authorship is retained in [NOTICE.md](NOTICE.md).
