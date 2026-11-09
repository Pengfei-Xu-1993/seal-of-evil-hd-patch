# Seal of Evil Save Editor

A save editor for the verified Steam version of **Seal of Evil**, with readable character and skill properties, equipment bonuses, a complete item catalog, and original-game item generation.

Developed and implemented by **OpenAI Codex**. The publisher reviewed the work and did not write the code.

## Download

The binary release is being prepared with stronger item presets. The current source and table-limit commands are documented below.

Download **[SealSaveEditor.zip from the Save Editor release](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/save-editor-v1.0)** and extract the complete archive. Run `SealSaveEditor.exe`; keep `SealItemWorker.exe` in the same folder.

No installer or Python installation is required. The application uses the .NET Framework included with Windows 11. GitHub's automatically generated source archives are for building the program; use the attached ZIP to run it directly.

## What you can do

- Edit character level, experience, health, energy, money, five-element attributes and verified combat properties.
- Edit learned skills and their individual components with readable explanations. Shared display names do not merge distinct skill records.
- Change equipment and crafting-material bonuses, including adding or removing actual bonus records.
- Browse all **657 ordinary item templates**, including hidden equipment, fixed and random bonuses, requirements, sockets and original generation information.
- **Add new items** to a chosen party member without consuming an existing object.
- **Replace an item** with another original template, such as changing a staff into iron ore or creating Lan Wei's hidden weapon, Demonic Flame. Generate and review the preview before applying it.
- Use Chinese or English editor views. Item searches accept Chinese display names, installed English names and native aliases.
- View the current map and edit coordinates within that map.
- Undo or redo applied edits, export a separate edited copy, and save with an automatic whole-slot backup.

Item previews use the verified original engine from your own installation. Generation inputs range from 1 to 200; some item types retain their original native level limits. Another 16 illustration/description entries are read-only references rather than inventory templates.

## Start editing

The editor opens in Chinese by default. Choose **English** in the header language selector to switch the interface.

1. Close the game.
2. Select a detected save slot, or open its `System/game/player_XX.gam`. Keep the matching `System/user/player_XX.use` and companion slot files together.
3. Select a character, skill or inventory item, choose a property, and use the editor on the right. **Apply value** changes the working copy.
4. Choose **Save with backup** to write the slot, or **Export copy** to keep an edited copy elsewhere.
5. Start the game and load the edited slot. **Restore backup** restores a complete recorded slot snapshot.

Backups are stored beside the GAM files in `SealSaveEditor-backups`. Saving checks for source files changed since opening, validates the edited pair and rolls back its file replacements if a write fails. Undo and Redo apply to unsaved working changes.

## Item shortcuts

In **Items & equipment**, select the item and open **Shortcuts**.

| Command | Shortcut | Effect |
| --- | --- | --- |
| Add all equipment bonuses at table maxima | `Ctrl+Alt+A` | Adds or raises the 29 supported persistent bonus types using declared original-table maxima. |
| Set level and element requirements to 1 | `Ctrl+Alt+1` | Sets both current and base level/elemental-attribute requirements to 1. |

Each changed command is one complete Undo/Redo action. Repeating it adds no duplicate preset records or history. Existing higher values and special, elemental, growing, disabled or conditional records are preserved. Dispel, revival, restore-health and restore-energy consumable actions are excluded from permanent bonuses.

These commands change the working copy; **Save with backup** writes the slot. They are available in the item tab, and shortcut handlers reject hidden or modal-disabled editor windows.

## Chinese item names

All 657 templates have Chinese editor display names. Of these, 208 use reviewed Chinese annotations read from the installed tables; 449 use explicitly labeled editor reference translations, including the 20 hidden set pieces. Reference translations are not claimed to reproduce the original Chinese release.

This affects editor presentation. Native item names, aliases, IDs and generated save payloads retain their original identity. It does not translate the game's interface.

## Supported build and limits

The editor was tested on Windows 11 with the Steam version-30 EASYRPG/USER save format. Native item generation checks the installed `ERRelive.dll` build and rejects unsupported binaries. No original game files, data tables or saves are included.

- Cross-map relocation and unknown or structured script/model fields remain read-only.
- The game can recalculate character, skill and conditional values. Editing a displayed description alone does not change the underlying effect.
- Bonus presets use declared original-table maxima, which are not engine-wide hard limits.
- The new bulk-bonus preset passed editor/save tests; its complete equipment and combat behavior has not yet had a separate running-game test.
- The HD patch and its tooltip-canvas update are separate downloads.

See [validation and evidence limits](validation.md) and [development attribution](ATTRIBUTION.md).

## Build from source

Open PowerShell in the source folder containing `build.ps1`, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script uses the Windows .NET Framework C# compiler and builds both executables without downloading dependencies. Keep the resulting editor and x86 item worker together.
