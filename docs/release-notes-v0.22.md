# Seal of Evil HD Patch v0.22

A **Windows 11 startup compatibility fix** for the verified Steam build of *Seal of Evil*, with **1080p, 1440p (2K), 4K, borderless fullscreen, and enhanced opening and main-menu artwork**. The patch addresses the legacy 16-bit display-mode startup failure without requiring Windows compatibility mode or a 640x480 desktop setting.

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

Download the complete **`SealHD_v0.22.zip`** installation package. Extract it fully, exit the game, and run `Install.cmd`. For another Steam library, use `-GameDirectory` to specify the folder containing `SOE.EXE`. See the [installation instructions](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/main/README.md).

## Features

- Borderless fullscreen with centered ESC, Options, and Load Game dialogs and mouse coordinates mapped to the current game canvas.
- Five actual rendering resolutions in Options: 800x600, 1024x768, 1920x1080, 2560x1440, and 3840x2160. Apply, save, and cancel are supported; the default is 1080p.
- An enhanced main-menu background, with the original central objects and decorations as the restoration target, sparse widescreen extensions, removal of the red demo-version label, and a localized campfire effect.
- An enhanced opening title and three static brand cards. The cards run for approximately 19.167 seconds in total and support Esc, Space, or mouse-click skip. The game window retains foreground ownership during their presentation.
- An installer that manages all five PNG assets, retains the first-install backup during upgrades, and restores recorded original files during uninstall while keeping the backup.

## Testing and limitations

These results were checked on the development machine; other game builds and hardware configurations have not been verified.

v0.22 passed Steam main-menu startup, Shift+Tab overlay opening and closing, and returned-menu Options and directional resolution selection. Steam recorded exit code 0, and the audit window showed no related new crash events or dumps. The available records did not establish what initiated that exit.

World-resolution and input evidence comes from v0.21 direct-launch testing: same-process 4K > 1440p > 1080p changes and saving, restart persistence, character movement, centered ESC menus, Cancel, and 20 ms clicks were exercised. v0.22 retains these core paths. **A Steam-launched world session has not been separately retested.**

v0.22 testing also covered the enhanced title, all three foreground fullscreen brand cards, Esc skip, sustained main-menu presentation, Options, and Load Game. Isolated Windows PowerShell 5.1 tests passed backup-record upgrades, repeated installation, and uninstall restoration. See the [validation summary](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/main/docs/validation.txt).

The development machine showed approximately 13 FPS at 4K with Options open and 39.5 FPS at 1080p with Options closed. The different UI states prevent a controlled performance comparison. This release defaults to 1080p.

All five artwork files are 3840x2160 exports. Four ImageGen originals are 1672x941; the title original is 1671x941. High-quality bicubic resampling produced the final 4K files. See the [AI disclosure](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/main/AI_DISCLOSURE.md) and [artwork provenance](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/tree/main/provenance).

## File verification

The complete ZIP is **47,575,985 bytes**, containing **26 files**, including a `manifest.json` that records the other 25 files.

```text
SealHD_v0.22.zip
SHA256 30CF8156225D758334955A379D974296F1F02D61CA23E2AA5BB352702D33DA9C

payload/dinput8.dll
186880 bytes
SHA256 3F921349A94ADC578316C5C6F235B8480E905B3104CB512E876D9CF9546AE2A3
```

The published package was extracted and all per-file sizes and hashes matched its manifest. Its seven source files matched the corresponding build sources. Repository documentation can be updated independently of that ZIP.

The installer accepts only the following original game build:

```text
SOE.EXE
452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B

Gfx3d.dll
1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078
```

The package includes unmodified dgVoodoo 2.87.6 x86 components from Dege's official release. Original game executables, user saves, and private test screenshots are excluded. See [third-party attribution](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/main/THIRD_PARTY.md).
