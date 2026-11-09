# Seal of Evil Save Editor

Download **SealSaveEditor.zip**, extract it, and run **SealSaveEditor.exe**. Keep **SealItemWorker.exe** beside it. Close the game before saving edits.

- Edit character stats, money, skills, equipment and crafting-material bonuses.
- Browse all 657 original item templates; generate a preview to add new items or replace existing ones, including hidden equipment.
- Chinese and English editor views, Chinese item names, and searches by Chinese name, English name or alias.
- Automatic whole-slot backups, export copies, and Undo/Redo.
- In **Items & equipment > Shortcuts**, use **Ctrl+Alt+A** for the new **Extreme equipment bonuses**, **Ctrl+Alt+T** for the separate original-table preset, or **Ctrl+Alt+1** to set current and base level/element requirements to 1.
- Extreme examples: **+4,096 maximum health** instead of the table's 600, **+4,096 attack** instead of 120, and **+32,768 to each of five defenses** instead of 120. A read-only comparison shows all 29 values, their units and the evidence behind them.

Existing special records and higher values are preserved; repeating a preset adds no duplicate preset records. The extreme values combine effective caps with strong operational values budgeted for seven reinforced equipment slots. The health/energy budget accounts for the character panel's signed 16-bit snapshots. These are not global absolute maxima, and movement/attack speeds can become very fast. See the [values and scope](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/main/save-editor/extreme-values.md).

Tested on Windows 11 with the verified Steam save format. Cross-map relocation remains read-only. The [validation summary](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/main/save-editor/validation.md) separates completed checks from current extreme-preset verification status.

**Developed and implemented by OpenAI Codex. The publisher reviewed the work and did not write the code.** No original game binaries, data tables or saved games are included.

The HD patch and its tooltip-canvas update are separate downloads.
