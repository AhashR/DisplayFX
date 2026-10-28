# Review resolution for DisplayFX 2.1.2

This maps the 25 findings from the October 2026 review to the changes in this release. Regression tests use fake displays and isolated settings; no automated hardware test changes physical monitor settings.

| Finding | Resolution |
| --- | --- |
| 1. UI hardware blocking | GPU profiles run off the dispatcher with serialization and superseded-request checks. The popup enumerates and initializes monitors asynchronously. |
| 2. Stale slider completion | Completion and persistence check the newest known hardware value; final slider feedback reconciles with that value. |
| 3. Stale refresh | Local edit versions and controller generations reject obsolete reads. |
| 4. Sleep during lock contention | Sleep is checked after locking and immediately before native brightness calls. |
| 5. Relative shortcut race | Relative read/modify/write shares the same per-monitor lock as absolute writes. |
| 6. Invalidated active profiles | Hardware invalidation forces reapplication, including the retained manual profile. |
| 7. Import/active-color mismatch | Import invalidates old in-flight models and applies the final active profile after rebuilding. |
| 8. Consumed schedule failures | Failed/missing outputs receive bounded per-output retries; newer writes cancel obsolete retry targets. |
| 9. Duplicate executable links | Saving a conflict is rejected; existing ambiguous links are reported and do not choose a list-order winner. |
| 10. Weak startup validation | Stored settings share structural/range/capacity validation with imports, with file/depth limits and duplicate-property rejection. Existing shortcut/link conflicts remain recoverable in the UI. |
| 11. Lost paginated validation | Raw schedule input lives in the draft model, so invalid input survives view destruction and blocks saving. |
| 12. Missing logs | Bounded rotating files are configured in the per-user Logs folder; Settings can open them. |
| 13. Modal automatic failures | Color application reports nonmodal status; automatic retry uses a 30-second backoff and an in-flight gate. |
| 14. Missing rollback snapshot | A combined gamma/vibrance operation is refused before writing if its previous gamma cannot be read. |
| 15. Startup entry after uninstall | Inno Setup removes the application's own per-user Run value. |
| 16. Unsafe brightness safeguard | Removed physical brightness compensation from profiles. Added opt-in read-only immediate/delayed observations and diagnostic warnings. The original driver/monitor reset remains hardware-dependent and is not claimed to be reproduced. |
| 17. Ambiguous cloned outputs | Independent brightness reads/writes are rejected when more than one physical target maps to the logical output. |
| 18. Small/high-DPI Settings | Work-area caps plus horizontal/vertical scrolling keep content reachable. |
| 19. Popup monitor | Position follows the click's monitor/work area using native coordinates and per-window DPI. |
| 20. Profile export round-trip | Added validated single-profile color import, with fresh identity/name and without stealing existing triggers. |
| 21. Draft loss on restore | A concrete discard warning is shown before committing a restore when profile drafts exist. |
| 22. Forced collection/trimming | Normal GC now owns memory policy; view lifecycle calls no longer force collections or trim working sets. |
| 23. Brightness terminology | Profile controls and accessibility names say Color brightness, with a backlight explanation. |
| 24. Monitor continuity | Valid EDID serial identities match a unique disconnected monitor across path changes. Missing/duplicate identities do not guess. |
| 25. Regression/CI coverage | Added race, retry, invalidation, import, identity, logging, scrolling, and raw-input checks, plus Windows CI and a verified-asset release workflow. |

The original physical reset, actual multi-DPI popup placement, EDID availability through specific docks, and native GPU/DDC firmware behavior still require verification on supported hardware. Profiles themselves issue no physical brightness commands.
