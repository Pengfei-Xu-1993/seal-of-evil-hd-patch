# Portability beta validation

The v0.25-beta.1 installer is the audited local candidate uploaded unchanged. This record distinguishes actual game execution from installer fixtures and UI checks.

## Audit findings

The installed production proxy, full v0.24 ZIP and DialogFix proxy matched SHA256 `54A78C9003CF8B373EFCA9A5C6187E360060B3DBD9AD22E5D5D1B29F90DEAB74`. The working production sources matched published commit `67203cd6b3cf921a1e60261146ae8c3186ff865a`. No different, privately loaded patch DLL explained the successful tests.

The acceptance-test difference was the executable: successful recent tests used a modern embedded manifest that v0.24 did not apply to the original game executable. No private IV50 decoder was found on the development computer either.

## Actual runtime experiments

Independent test directories used the verified original vendor executable/modules, pre-patch wrapper/configuration backups, and independent writable configuration, movies and save directories. Static game-asset directories were shared through junctions; these were not fresh Steam downloads of every asset.

| Experiment | Observed result |
| --- | --- |
| Original executable plus complete v0.24 package | Reproduced green/purple menu corruption. Actually loaded patch/wrapper DLLs matched the release. D3D8 factory slot 15 belonged to `apphelp.dll`, no proxy device record was found, and the HD compositor did not run despite keyed artwork being used. |
| Same installation with `MainMenu=0` | Removed the keyed green block, but original artwork and controls still had purple/green corruption. Disabling artwork did not resolve the underlying color path. |
| Fresh original executable with modern manifest before first launch, same v0.24 DLL | Correct fullscreen HD menu colors, active proxy factory/device slots, and successful CreateDevice, Present and composition records. |
| Modern manifest plus new proxy/video probe | Correct HD menu; the unsupported Start.dat graph was explicitly skipped, and original movie file hashes stayed unchanged. |
| Actual unified-installer backend on an unpatched fixture | Complete installation, actual game startup/menu, and restoration passed. All 14 managed paths returned to original hashes or original absence. |

Loading `apphelp.dll` by itself is not a failure indicator: it was present in successful runs too. Actual IAT/vtable targets and frame counters provided the useful distinction. The failed production snapshot does not establish whether hook installation initially failed or a later component changed it.

The development machine did not reproduce the remote machine's persistent state-11 stall. Its original IV50 playback failed and continued to the menu. DirectShow graph construction returned partial rendering without a connected video decoder; this supports the missing-video-support diagnosis but does not prove the remote thread's exact blocking cause. The probe checks graph construction, not complete playback.

## Installer checks

- **64 backend assertions:** installation, 1440p in-place update, first-install backup preservation, exact restoration, loader conflict handling, PE/resource preservation, INI preservation and MainMenu safety default, permission preflight, shared registry views, partial registry-write failure, rollback after registry/DLL/EXE/state/final verification, and rejected invalid/path-traversal/duplicate ZIP entries. Registry writes used memory fixtures; original files were only read/copied.
- **17 real-backend GUI assertions:** actual WinForms UI, installer service and embedded payload, private-desktop 4K installation, installed-state inspection, Chinese/English fallback disclosure, and restoration. Real Windows registry values were read; writes were prohibited. This smoke preceded final configuration-default and restore-report wording changes, which the final backend suite covers.
- **111 earlier UI assertions:** asynchronous progress, duplicate-operation/close blocking, stale-inspection rejection, conflict/restore confirmation, and bilingual layouts using a stub API. These are UI evidence only.
- **Native checks:** 17 actual artwork-dispatch routing assertions and 14 movie-guard scenarios, original AVI-header checks and real graph-only helper calls, including unsupported/missing/error/timeout handling and original-file preservation.

The final source verifies every non-resource PE section's contents when embedding the game manifest, protecting code/data layout and unrelated resource contents.

## Remaining limits

The remote Intel i7-8550U computer and the final elevated EXE's normal double-click/UAC flow have not been validated end to end. Protected-HKLM write denial was observed; preflight and rollback were tested, but removal of an existing protected HKLM override was not exercised in the actual game experiment.

No new full-campaign, all-resolution gameplay, or in-game ending-movie acceptance test was performed for this beta. Original opening/ending videos are skipped if IV50 decoding is unavailable; no decoder is bundled. Manually renamed movies remain unchanged.

These results support a test release, not a declaration that every remote compatibility problem is resolved. Stable main and v0.24 assets remain unchanged.
