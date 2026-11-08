# Seal of Evil HD Patch

A **Windows 11 startup compatibility fix** for the verified Steam build of *Seal of Evil*, with **1080p, 1440p (2K), 4K, borderless fullscreen, centered menus, and enhanced opening and main-menu artwork**.

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

## Download and install

Download **`SealHD_v0.22.zip`** from the [v0.22 Release](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.22). Extract the complete archive and run `Install.cmd`; no compilation is required. GitHub's automatically generated **Source code** archives contain the repository, without the complete installation `payload`.

1. Exit the game.
2. Extract the complete installation package into one folder, keeping its `payload` folder.
3. Double-click `Install.cmd`. The installer uses a preset game path and does not automatically discover Steam libraries. For another location, specify the path as shown below.
4. Start the game through Steam. The default rendering resolution is **1920x1080**.

Open PowerShell in the extracted package folder and replace the example with your game directory: the folder containing `SOE.EXE`.

```powershell
.\Install.cmd -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"
```

The patch addresses the legacy 16-bit display-mode startup failure. Installation does not require Windows compatibility mode or the "Run in 640 x 480 screen resolution" setting. The game loads the patch through its local `dinput8.dll`, with dgVoodoo 2.87.6 providing Direct3D 11 presentation.

## Display and controls

- Borderless fullscreen, with mouse coordinates mapped to the current game canvas and centered ESC, Options, and Load Game dialogs.
- Five actual rendering resolutions under `Options > Screen Resolution`:

| Option | Rendering size |
| --- | --- |
| 800x600 | 800x600 |
| 1024x768 | 1024x768 |
| 1080p | 1920x1080 |
| 1440p (called 2K in this project) | 2560x1440 |
| 4K | 3840x2160 |

- Use the left and right controls to select a resolution. `OK` applies and saves it; `Cancel` preserves the active setting. `ProfileIndex` in the game's `SealHD.ini` records the selection.
- An enhanced main-menu background uses GPU compositing, retains the game's menu interactions, and includes a localized campfire effect.
- The enhanced opening includes a title image and three static brand cards. The cards run for approximately 19.167 seconds in total; Esc, Space, or a mouse click skips them.

Lower rendering resolutions remain available as options. Installation and startup do not require changing the desktop to those sizes.

## Artwork provenance

All five PNG files are exported at **3840x2160**. The main-menu remaster uses the supplied original as its reference, with preservation of the central objects, decorations, and textures as the restoration target. The added side areas contain fewer objects, and the red demo-version label has been removed. The title remaster retains the original three hanging ornaments.

ImageGen edited these images. Its generated originals are **1672x941** for the main menu and three brand cards, and **1671x941** for the title. High-quality bicubic resampling produced the final 4K files; 4K describes the export dimensions. Prompts, original dimensions, and exported asset hashes are in [provenance](provenance/). See the [AI disclosure](AI_DISCLOSURE.md) for development attribution.

## Verified behavior and performance

Testing used a Windows 11 development machine. Other game builds and hardware configurations have not been verified. Detailed evidence is summarized in [docs/validation.txt](docs/validation.txt).

| Scope | Observed result |
| --- | --- |
| v0.22 Steam startup | Reached the main menu; Steam tracked the game process |
| v0.22 Steam overlay | Shift+Tab opened and closed the complete overlay; Options and both resolution controls worked afterward |
| v0.22 exit | Steam recorded exit code 0, with no related new crash events or dumps in the audit window; the exit initiator was not established |
| World resolution and input | v0.21 direct-launch testing covered same-process 4K > 1440p > 1080p changes, saving, restart persistence, movement, centered ESC menus, Cancel, and short clicks |
| Enhanced opening and menu | Foreground fullscreen title and all three brand cards, Esc skip, Options, Load Game, and sustained menu presentation were exercised |
| Installation and restoration | Isolated Windows PowerShell 5.1 tests passed 11-to-12 backup-record migration, repeated installation, and uninstall restoration |

v0.22 retains the tested world-resolution and input paths. **A Steam-launched world session has not been separately retested.**

The development machine showed approximately **13 FPS** at 4K with Options open and **39.5 FPS** at 1080p with Options closed. The different UI states prevent a controlled resolution-performance comparison. The default is 1080p; select higher resolutions according to performance on your machine.

## Supported game build

The installer checks these original game files against their SHA256 hashes before installation. Other builds have not been verified.

| Original file | SHA256 |
| --- | --- |
| `SOE.EXE` | `452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B` |
| `Gfx3d.dll` | `1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078` |

## Set the initial resolution or uninstall

Exit the game first. Run these commands from the complete installation package, using the same actual game directory for each operation.

Set the initial resolution to 1440p:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Manage-SealHD.ps1 -Action Resolution -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil" -Width 2560 -Height 1440
```

Uninstall and restore the managed files from before the first installation:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Manage-SealHD.ps1 -Action Uninstall -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"
```

The first-install backup is kept in the game's `SealHD-backup` folder. Upgrades retain that backup and add records for newly managed assets. Uninstall keeps the backup. If a newly added file did not exist before installation and has since been modified, uninstall preserves it and reports that decision.

## Source and build

The repository and complete installation package have separate layouts:

| Path | Contents |
| --- | --- |
| `source/` | Seven files: C++ source, export definition, three headers, build script, and initial configuration |
| `packaging/` | `Manage-SealHD.ps1` and `Install.cmd`; installation requires the complete Release `payload` |
| `docs/` | Validation summary, release notes, and announcement text |
| `provenance/` | Artwork prompts, dimensions, and asset hashes |
| `THIRD_PARTY.md` | Third-party component and artwork attribution |

Building requires the MSVC C++ tools and Windows SDK from Visual Studio 2022 Build Tools, targeting **x86 / 32-bit**. The source contains x86-specific implementations.

```powershell
cd source
.\build.cmd
```

`build.cmd` currently calls `vcvars32.bat` at the default VS 2022 Build Tools location. Adjust the toolchain path if Visual Studio is installed elsewhere. The build produces `source/dinput8.dll`. The complete Release package supplies the third-party DLLs and artwork.

The v0.22 package's `dinput8.dll` is 186,880 bytes, with SHA256:

```text
3F921349A94ADC578316C5C6F235B8480E905B3104CB512E876D9CF9546AE2A3
```

## Release files and attribution

`SealHD_v0.22.zip` is 47,575,985 bytes, with SHA256:

```text
30CF8156225D758334955A379D974296F1F02D61CA23E2AA5BB352702D33DA9C
```

The package includes a per-file `manifest.json` covering patch source, installation scripts, configuration, third-party presentation components, and five artwork assets. Its manifest describes the published ZIP; repository documentation can be updated independently. Original game executables and user saves are supplied by the user's own installation.

dgVoodoo 2.87.6 comes from [Dege's official release](https://github.com/dege-diosg/dgVoodoo2/releases/tag/2.87.6). Its three bundled x86 DLLs are unmodified. Original game artwork, brands, and third-party components belong to their respective rights holders. See [third-party attribution](THIRD_PARTY.md).
