# Seal of Evil HD Patch - Complete Installer Beta

**[Download the complete EXE installer beta](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/download/v0.25-beta.1/SealHDSetup-Portable-Test.exe)** from the [test release](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.25-beta.1).

Windows 11 compatibility improvements, 1080p / 1440p (2K) / 4K resolution options, borderless fullscreen, HD opening screens, and widescreen main-menu artwork are included in **one graphical installer**. No earlier patch package or separate update is required.

Close the game, run the EXE, select the folder containing `SOE.EXE`, choose a resolution, and install. Backup and restore are included, with Chinese and English interfaces. End users do not need CMD or PowerShell. Administrator permission is requested for existing game-specific compatibility settings.

This beta applies the missing modern Windows manifest to the verified local game executable, handles the selected game's legacy color-mitigation flag, and keeps original artwork if the HD presentation path is unavailable. **If IV50 decoding is unavailable, original opening and ending videos are skipped automatically; their files are preserved.** No decoder is installed or bundled, and manually renamed movies are left unchanged.

Local actual-game startup/menu and restoration passed, with all 14 managed paths restored exactly. Backend tests passed 64 assertions and a real-backend GUI smoke passed 17. The **remote Intel i7-8550U computer** and **final elevated/UAC installation flow** remain unverified. See [beta validation](docs/portability-beta-validation.md).

**Developed and implemented by OpenAI Codex. The project owner only reviews and approves the work and did not write the code.**

The beta tag includes the matching native patch and installer source. See [build instructions](installer/README.md). The stable `main` branch and existing stable release remain unchanged.

Installer SHA256: `0E6523E32828F63B1057F3A93A45EB4D62168A44C9BB612E8522F65CB7703032`.

## Previous stable release documentation

Play the Steam version of **Seal of Evil** on **Windows 11**, with **1080p, 1440p (2K), and 4K** resolution options, borderless fullscreen, enhanced opening screens, and a widescreen main-menu background.

**Developed and implemented by OpenAI Codex. The publisher only reviewed the work and did not write the code.**

## Downloads

| Download | What to choose |
| --- | --- |
| **[Full HD patch](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.24)** | Download `SealHD_v0.24.zip` for the complete installation, including artwork and presentation components. |
| **[Update an existing HD installation](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/download/v0.24/SealHD-DialogFix.zip)** | Small update that adds the fullscreen confirmation fix and retains your existing resolution, artwork and configuration. Requires the complete HD patch to be installed already. |
| **[Save Editor](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/save-editor-v1.1)** | Download `SealSaveEditor.zip` for the editor, its two native helpers, source and English instructions. |

Use the application ZIP attached to the relevant release. GitHub's automatic **Source code** archives are for building the tools and do not contain the complete installed HD payload.

## HD patch

- Fixes the verified Steam build's Windows 11 startup failure without requiring Windows compatibility mode or the 640x480 compatibility setting.
- Adds **1920x1080**, **2560x1440**, and **3840x2160** to the game's resolution options, with borderless fullscreen.
- Adds enhanced opening screens and a widescreen campfire main-menu background, supplied as **3840x2160** artwork files.
- Fixes corrupted skill-tooltip backgrounds and their reproduced multi-second hover freezes, retaining the original translucent background.
- Keeps overwrite-save confirmations over the fullscreen game instead of minimizing the game to the desktop. Confirm, Cancel and normal Alt+Tab switching were tested.

To install, exit the game, extract **all** of `SealHD_v0.24.zip`, and run `Install.cmd`. Start normally through Steam, then select a resolution under **Options > Screen Resolution**. The default is 1080p. Instructions cover other Steam library locations and restoring the previous files.

Existing HD users can extract `SealHD-DialogFix.zip` and run its `Install.cmd` to retain their current settings. The update backs up the previous proxy and preserves the original-install backup history. It does not manage saved games.

The artwork uses AI-assisted restoration and widescreen extension. Generated source images were approximately 1672x941 and were resampled to 3840x2160; **4K describes the delivered dimensions**, not native 4K generation. See [artwork provenance](provenance/) and [AI disclosure](AI_DISCLOSURE.md).

Higher resolutions increase the old renderer's workload. Earlier 4K tests submitted approximately 10 frames per second on the development machine. Start with 1080p and choose a resolution that performs well on your system. The confirmation fix preserves the small original dialog layout; the tested English message still clips its final words.

## Optional save editor

The editor now displays **original skill and equipment icons**, prepares item previews automatically, and supports continuous item addition without closing its browser.

- Edit readable character properties, learned skills and equipment/material bonus records, with Undo/Redo and whole-slot backups.
- Browse **657 ordinary item templates**, including hidden equipment, and add or replace objects using their original native generated state. Use quick level-99 addition or choose another generation level.
- Generate **218 concrete native skill books**, including learning books, upgrade books, class manuals and special/crafting books. These retain the real skill bindings and use behavior.
- Learn the selected character's class skills or maximize that class's skill tree using the original rank limits and associated forms.
- Add original companions with their native starting equipment and skills. Early addition does not complete their later recruitment quests.
- Enable second-playthrough eligibility while retaining current progress. This does not automatically satisfy the hidden ending's other conditions.
- Edit game **Data TXT** through a table with Chinese field explanations or the original text view. Saving creates a backup; changing these files affects the installation, not only one save.

Extract the complete editor ZIP and keep `SealItemWorker.exe` and `SealActorWorker.exe` beside `SealSaveEditor.exe`. Chinese is the default interface; English is available. Close the game before saving edits. The editor reads original definitions and icons from your own installation and is optional for the HD patch.

Coordinates can be edited within the current map; cross-map relocation is read only. Skill activation still follows original class and element conditions. Later story scripts can recruit or replace an early-added companion. Validation covers the tested workflows, not a complete campaign after every combination of edits.

## Requirements and source

You need your own supported Steam installation. The public editor contains **no original game binaries, data tables, resource icons or user saves**. The full HD package includes its documented presentation components and enhanced artwork. See [third-party attribution](THIRD_PARTY.md).

Source and manuals: [HD installation/build](docs/installation.txt), [HD validation](docs/validation.txt), [editor manual/build](save-editor/README.md), [editor validation](save-editor/validation.md).
