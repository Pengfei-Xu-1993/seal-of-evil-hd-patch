# Seal of Evil HD Patch

Windows 11 compatibility improvements, **18 resolution choices**, borderless fullscreen, HD opening screens, and a widescreen main menu with animated fire, rising steam, a swaying curtain, and changing firelight.

**Developed and implemented by OpenAI Codex. The project owner only reviews and approves the work and did not write the code.**

## Download and install

**[Download the complete EXE installer](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/download/v0.26/SealHDSetup.exe)** from the [v0.26 release](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.26).

1. Close the game and run `SealHDSetup.exe`.
2. Select the game folder containing `SOE.EXE`.
3. Choose a resolution and install. The default is 1080p.
4. Start the game normally through Steam.

All HD-patch components are included in one graphical installer. No earlier patch package, separate update, CMD, or PowerShell commands are required. The installer supports backup and restore, with Chinese and English interfaces. Windows administrator permission is requested for game-specific compatibility settings.

Existing users can run the same installer to update. It re-enables the HD menu after the temporary `MainMenu=0` diagnostic setting. It leaves manually renamed movie files unchanged; after installation, a previously renamed `Movie/Start.dat.SealHD-disabled` can be renamed back to `Start.dat` if that name is absent.

## Optional Chinese localization and skill mod

[Download the optional add-on packages](addons/README.md) for Chinese text and an optional no-cooldown/360-degree skill mod. Install the HD patch first, then the Chinese pack. The skill mod is separate and includes the latest Liang Hu quake (胆战心惊) and actor-support-list fixes.

Each ZIP includes installation, hash checks and restoration. They preserve the HD DLLs, artwork, display settings and personal saves. **The final actor-support and quake revision has been checked statically and at file level, but has not been re-tested in a running game.** See the add-on instructions and validation limits before using it.

## Included

- Windows 11 startup and color-path compatibility treatment for the supported Steam build, without requiring Windows compatibility mode or a 640x480 compatibility setting.
- 1080p, 1440p (2K), 4K, and common intermediate 16:9 and 16:10 resolutions in both the installer and game settings.
- Borderless fullscreen, adapted mouse positioning, centered menus, skill-tooltip fixes, and fullscreen save-confirmation handling.
- Enhanced opening screens and widescreen main-menu artwork, delivered as five 3840x2160 files.
- Original-frame-driven flames below and along the cauldron, steam above its rim, curtain movement, and local firelight changes on nearby objects. The HD textures are retained.

| Family | Available resolutions |
| --- | --- |
| Original 4:3 | 800x600, 1024x768 |
| 16:9 and common near-16:9 displays | 1280x720, 1360x768, 1366x768, 1536x864, 1600x900, 1920x1080, 2560x1440, 3840x2160 |
| 16:10 | 1280x800, 1440x900, 1600x1000, 1680x1050, 1920x1200, 2560x1600, 2880x1800, 3840x2400 |

Select a resolution under **Options > Screen Resolution**. Higher resolutions increase the old renderer's workload; start with 1080p and choose a size that performs well on your computer.

## Original videos and requirements

**If IV50 decoding is unavailable, original opening and ending videos are skipped automatically; their files are preserved.** No decoder is bundled or installed. The enhanced static opening screens remain a separate feature.

You need your own supported Steam installation. The installer verifies the game files before changing them and does not distribute a vendor game executable, saved games, original movies, or game data tables. It applies the compatibility manifest locally and provides a safe original-artwork fallback if HD presentation is unavailable.

The main-menu animation is reconstructed from original frame sequences in the player's installation. It is not a physical lighting simulation or a pixel-identical replacement of the original composition. The artwork uses AI-assisted restoration and widescreen extension; **4K describes the delivered dimensions**, not native 4K generation. See [artwork provenance](provenance/), [AI disclosure](AI_DISCLOSURE.md), and [third-party attribution](THIRD_PARTY.md).

## Instructions, validation, and source

- [Installation and restoration](docs/installation.txt)
- [Resolution and animation validation](docs/resolution-scene-candidate.md)
- [Installer build instructions](installer/README.md)
- [Optional save editor](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/save-editor-v1.1)

The release tag contains the matching native patch and installer sources. GitHub's automatic **Source code** archives are for developers; download the EXE to install the patch.

Installer SHA256:

```text
E41845600EC704AEBBF90BF311B9C0E31EF38C21BE2971BA28AF8A63D906F9F4
```
