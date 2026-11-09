# v0.26 resolution and scene-animation validation

The v0.26 release publishes the validated complete-animation installer unchanged, renamed to `SealHDSetup.exe`. The earlier v0.25-beta.1 artifacts remain available separately. This record distinguishes representative actual-game tests, installer fixtures, and GPU/pixel analysis from untested workflows.

## Resolutions

The game settings and graphical installer share `config/resolutions.json`. Both expose the same 18 modes, in increasing size order:

| Family | Modes |
| --- | --- |
| Original 4:3 | 800x600, 1024x768 |
| 16:9 and common near-16:9 displays | 1280x720, 1360x768, 1366x768, 1536x864, 1600x900, 1920x1080, 2560x1440, 3840x2160 |
| 16:10 | 1280x800, 1440x900, 1600x1000, 1680x1050, 1920x1200, 2560x1600, 2880x1800, 3840x2400 |

The default remains 1080p. Existing profile IDs 0-4 retain their old dimensions; new modes are appended as stable IDs while display order is separate. A valid stored profile ID remains authoritative, preserving earlier save/restart semantics. Invalid or custom IDs use explicit dimensions. Selecting a new mode does not require editing INI files.

`config/Generate-ResolutionProfiles.ps1` produces the native and C# catalogs and the wrapper's 16 extra wide modes. `-Check` detects stale outputs. The wrapper's documented maximum of 16 extra entries is respected; the two original modes are already enumerated normally.

## Main-menu animation

The HD artwork remains unchanged. Runtime animation uses original resources from the player's installation; original game textures are not added to the package.

- The original five cauldron frames drive lower fire, flames rising along both sides, and neutral steam above the rim.
- The original nine environment frames independently drive local warm-light changes on the curtain, wall, ground and cauldron.
- Curtain movement follows measured original-frame displacement: the upper edge remains fixed while the lower cloth sways gently. It uses the HD texture rather than placing the old low-resolution curtain over it.
- Fire and steam are positioned using cauldron landmarks. The old generic fire ripple is removed. The cauldron, wood and other static objects are not geometrically warped.

The animation is a reconstruction driven by the original frame sequences and localized masks, not a new physical lighting simulation or a pixel-identical replacement of the original composition. `FireAnimation=0` preserves static artwork. Missing source data or failed graphics initialization falls back safely.

## Validation

The native profile/INI/selection/geometry harness passed 495 assertions, including all 18 modes, unchanged legacy IDs, custom dimensions, Cancel/reopen behavior, and rejected partial memory patches. The installer passed 113 actual GUI/backend assertions and 64 backend regression checks using the expanded-resolution payload. These checks use isolated files and prohibit real registry writes.

Actual local game execution verified successful world backbuffer changes to 1366x768 and 1680x1050, with normal colors, centered menus and input. A later actual game restart loaded the saved 1680x1050 profile. The final animation DLL also loaded a world at 1280x720 with a successful device reset. These are representative runtime checks, not a claim that every mode received a full gameplay test.

Original artwork was observed in an isolated game with HD menu substitution disabled, and its five- and nine-frame sequences were independently inspected. The final animation was then recorded in the actual local game. GPU readback tests separately verified effect bounds, UI occlusion, fixed curtain anchors, pipeline-state restoration and static fallbacks. Per-pixel assertions are not counted as separate gameplay scenarios.

The final graphical installer was inspected without execution to verify its embedded payload, x86 GUI subsystem and administrator manifest. An isolated actual installer service install/inspect/restore cycle restored all 14 managed paths. The final elevated/UAC double-click path has not been tested end to end. The user reported that the earlier beta worked on the other computer; this expanded-animation release has not been independently tested there.

## Released artifact identities

| Artifact | SHA256 |
| --- | --- |
| Complete installer `SealHDSetup.exe` | `E41845600EC704AEBBF90BF311B9C0E31EF38C21BE2971BA28AF8A63D906F9F4` |
| Embedded native proxy | `0CF6800218B921B1949C27F689D9F3B2DFF483A4F1AAC28691A619AF06564E03` |
| Embedded payload ZIP | `AC7BEF898C4B6CA5462A6B2104779E6716AF0B0575EB44978AF0BD541197D1A5` |

The previous compatibility manifest, targeted color-mitigation handling, unsupported-IV50 movie fallback, tooltips and confirmation fixes are retained. Missing IV50 support still causes original opening/ending videos to be skipped with their files preserved; this release does not add a decoder.
