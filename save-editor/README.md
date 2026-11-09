<img src="assets/seal-editor-icon-ai.png" alt="Seal of Evil Save Editor icon" width="96" align="right">

# Seal of Evil Save Editor

Readable character and skill editing, original skill/equipment icons, native item and skill-book generation, companion addition, second-playthrough eligibility, and integrated game-data editing.

Developed and implemented by **OpenAI Codex**. The publisher only reviewed the work and did not write the code.

## Download and run

Download **[SealSaveEditor.zip](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/save-editor-v1.1)** and extract the entire archive. Run `SealSaveEditor.exe`, keeping **both** `SealItemWorker.exe` and `SealActorWorker.exe` beside it. No installer or Python installation is required; the application uses Windows .NET Framework.

Chinese is the default interface. Choose **English** in the header to switch. Definitions and original icons are read from your installed game; none of its original data or resource icons are bundled.

## Characters, skills and party

- Edit verified money, experience, level, health, mana, five-element attributes and combat properties through named fields. Unknown structures remain advanced or read only.
- Inspect main skills, associated casting forms and conditional enhancement components separately. A component's `-1` rank is not an unlearned main skill.
- **Learn class skills** adds the selected character's missing class skills and their original branches. **Max class skills** also learns missing skills and raises ranks to their native limits. Class and element conditions still govern activation.
- Add original companions with native starting equipment, skills and level-1 values, within the five-person party limit. Later story recruitment is not completed by this action and can recruit or replace them again.
- Enable second-playthrough eligibility while keeping the current story and map progress. It changes the run-count condition; quests, the story ring and other hidden-ending conditions are not forced.

## Inventory, equipment and books

- Browse all **657 ordinary item templates**, including hidden equipment, with original generated defaults, requirements and fixed/random effects. Another 16 illustration entries are read-only references.
- Add to a selected party member or replace an existing item with its complete native state. Selecting an item prepares a preview automatically. Choose a level and Add, or double-click/use **Quick add level 99**. The browser stays open for more additions.
- Add, remove or edit equipment and crafting-material bonus records. Original-table and stronger extreme presets remain separate choices. Existing above-preset and special records are preserved.
- The separate browser offers **218 concrete native skill books**: learning, upgrade, second/third-stage, class manuals and special/crafting books. Books keep their native level-1 defaults and exact subclass paths; the five generic class-book shells do not provide those bindings.
- Manuals attempt to learn their listed missing roots at rank 1 and add their specified branches. They do not maximize existing skills. Original book use and upgrade requirements remain in force.

Generation inputs for ordinary items cover 1..200; some types enforce their own native level. Catalog presence does not guarantee an item normally drops in the game. Chinese item names distinguish reviewed installed annotations from editor reference translations.

| Equipment shortcut | Effect |
| --- | --- |
| `Ctrl+Alt+A` | Strong extreme preset for supported persistent bonuses. |
| `Ctrl+Alt+T` | Original-table maxima for those bonuses. |
| `Ctrl+Alt+1` | Current/base level and element requirements set to 1. |

The extreme preset is a researched strong profile, not a universal mathematical maximum. See [preset values and limits](extreme-values.md), including the signed 16-bit character-panel resource budget.

## Game Data TXT

Open **Game data TXT...** to edit installed `Data/*.txt` files through tables with Chinese column explanations or the original text view. The editor preserves the original encoding, marker, comments and line endings when supported and creates backups before saving. Backup restoration creates its own recovery backup.

These files affect the installation and all relevant saves. Close the game before saving them. Unsupported structures are shown in the text view rather than guessed into a table.

## Save safely

1. Close the game and open the desired `System/game/player_XX.gam`, with its matching `System/user/player_XX.use` and companion files present.
2. Apply edits to the working draft. Individual changes and bulk actions support Undo/Redo.
3. Use **Save with backup** or **Export copy**, then restart the game and load the edited slot.

Whole-slot backups live beside the GAM files in `SealSaveEditor-backups`. Saving checks the opened files for concurrent changes and stages the GAM/USE pair. Cross-map relocation is read only; same-map coordinates remain editable.

## Build and validation

From this directory, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

This builds the editor and its two x86 native helpers with the Windows .NET Framework compiler. The included application icon is required by the build. The default output is `build/`.

The downloadable package also includes the source under `source/`; run its `build.ps1` from that folder to rebuild it. See [validation](validation.md), the [full package manual](studio-docs/README.txt), and [attribution](ATTRIBUTION.md). Tests distinguish native construction, actual GUI actions, and representative running-game use; they do not claim every book or a full edited campaign was played through.
