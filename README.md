# Seal of Evil HD Patch

Play the Steam version of **Seal of Evil** on **Windows 11**, with **1080p, 1440p (2K), and 4K** resolution options, borderless fullscreen, enhanced opening screens, and a widescreen main-menu background.

**Developed and implemented by OpenAI Codex. The publisher only reviewed the work and did not write the code.**

## Downloads

| Download | What it does |
| --- | --- |
| **[HD Patch v0.23](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.23)** | Full installation package: Windows 11 startup compatibility, high resolutions, enhanced artwork, and the tooltip fix. Download `SealHD_v0.23.zip`. |
| **[Save Editor](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/save-editor-v1.0)** | Character, skill and item editing, native item generation, and strong equipment presets. Download `SealSaveEditor.zip`. |

Download the application ZIP attached to the relevant release. GitHub's automatic **Source code** archives are not the ready-to-run installation packages.

## HD patch

- Fixes the verified Steam build's Windows 11 startup failure without requiring Windows compatibility mode or the 640x480 compatibility setting.
- Adds **1920x1080**, **2560x1440**, and **3840x2160** to the game's resolution options, with borderless fullscreen.
- Adds enhanced opening screens and a widescreen main-menu background, supplied as **3840x2160** artwork files.
- Fixes corrupted or striped skill-tooltip backgrounds and the associated multi-second freezes when hovering near the bottom of a high-resolution screen. The original translucent background is retained.

To install, exit the game, extract **all** of `SealHD_v0.23.zip`, and run `Install.cmd`. Start the game normally through Steam, then choose a resolution under **Options > Screen Resolution**. The default is 1080p. The release includes instructions for other Steam library locations and restoring the previous files.

The full package works for new installations. Existing HD patch users can alternatively use the smaller `SealHD-TooltipFix.zip` update from the same release to replace only the proxy DLL and keep their current configuration and artwork.

The artwork uses AI-assisted restoration and widescreen extension. Its generated source images were approximately 1672x941 and were resampled to 3840x2160; **4K describes the delivered file dimensions**, not native 4K generation. See [artwork provenance](provenance/) and the [AI disclosure](AI_DISCLOSURE.md).

Higher resolutions increase the old renderer's workload. The tooltip fix removes the reproduced multi-second fault; it does not make general 4K rendering faster. The tested machine still submitted approximately 10 frames per second at 4K. Start with 1080p and choose a resolution that performs well on your system.

## Optional save editor

The separate save editor supports character money, experience, level, health, mana, elemental attributes, verified skill values, inventory, equipment, and actual item bonus records. It can add or replace items from the installed game's **657 item templates**, preview their native generated defaults, and add or remove equipment bonuses. Coordinates can be edited within the current map; cross-map relocation is read only.

Extract the editor package and open `SealSaveEditor.exe`, keeping `SealItemWorker.exe` beside it. Chinese and English interface options are available. Close the game before saving changes, then use **Save with backup** or **Export copy**. Item generation reads definitions and the verified native engine from your own game installation. The editor is optional and is not required by the HD patch.

## Requirements and source

You need your own supported Steam game installation. The public packages contain **no original game executables, original game data tables, or user saves**. Source code is included with the release packages; original artwork and third-party components remain credited to their respective owners. See [third-party attribution](THIRD_PARTY.md).

Testing covered the verified Steam build on a Windows 11 development machine. Each release includes its validation scope and limitations; other builds and hardware are not guaranteed to behave identically.

Manuals and source: [HD installation and build](docs/installation.txt), [HD validation](docs/validation.txt), [save editor manual and build](save-editor/README.md), [save editor validation](save-editor/validation.md).
