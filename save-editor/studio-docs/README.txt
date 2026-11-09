SEAL OF EVIL SAVE EDITOR STUDIO

Developed and implemented by OpenAI Codex. The publisher reviews the work and
does not claim to have written the code.

GET STARTED

Extract the complete ZIP and run SealSaveEditor.exe. Keep SealItemWorker.exe
and SealActorWorker.exe beside it. No installer or separate Python installation
is needed. The application uses Windows .NET Framework and reads definitions
from your installed copy of Seal of Evil.

1. Select the game folder and a save slot. A slot includes its matching GAM/USE
   files and any FOG/LOG/MAP/QED/SCN companion files.
2. Select a character, skill or item. Edit its named properties using the input
   panel. Apply changes to the working draft, then use Save with backup.
3. Close the game before writing a save or changing game Data text. Start the
   game again and load the edited slot after saving.

Chinese explanations and item names are the default; English is available in
the editor. Original table annotations and editor reference translations are
identified where applicable. Reference translations are not presented as a
verified reproduction of the original Chinese retail localization.

CHARACTERS AND SKILLS

- Edit verified character values such as money, experience, levels, health,
  mana, five-element attributes and combat properties. Metal is an element;
  money is the separate currency value.
- Inspect and edit actual learned skills, associated casting variants and
  conditional enhancement branches. Fields without a verified meaning remain
  in Advanced or read-only views.
- Learn class skills and raise the class skill tree using the installed game's
  original generation rules and rank limits. These commands update the working
  save and support Undo/Redo. Original class, element and activation conditions
  still determine which skill branches are usable in the game.
- Add original companions with their native starting state, equipment and
  skills. They begin at their original level 1; edit or develop them afterward.
  This does not mark their later story recruitment as completed. Later scenes
  can still run that recruitment and may recruit or reset them again.
- Enable second-playthrough eligibility while retaining the current map,
  quests and progress. This changes the stored run-count condition; it does not
  complete quests, select an ending or satisfy every hidden-reward condition.

ITEMS, EQUIPMENT AND BOOKS

- Browse all 657 ordinary item-table templates, including hidden equipment,
  fixed bonuses, random candidates, requirements, sockets and conditional
  effects. Sixteen separate illustration/reference entries are read only.
- Add items without replacing existing gear. Selection prepares a native
  preview automatically. Double-click or use Quick add level 99 for repeated
  additions; the window remains open. Other generation levels can be selected.
  The native engine can force some types, including consumables, to their own
  original level regardless of the requested generation level.
- Replace an existing item with its complete generated native state. Additional
  old modifiers are discarded by default and can optionally be retained where
  supported. A name change alone is not an item-type replacement.
- Add, remove or edit real equipment/crafting-material modifier records. Table
  maximum and stronger preset actions are available. Strong presets use verified
  effective caps or bounded operational values; they are not a claim of a
  universal mathematical maximum. See the preset details for their conditions.
- The separate skill-book browser contains 218 concrete native books: learning,
  upgrade, second/third-stage, class manuals and special/crafting books. Choose
  a recipient, then double-click or Add repeatedly without closing the window.
  Books retain their original level-1 defaults and exact native subclass paths.
  The five generic class-book table shells are not usable bound-skill books.
- A class manual attempts to learn its listed unknown root skills at level 1
  and adds its specified enhancement branches. It does not automatically max
  already learned skills. The Wizard-class manual (Witch Scroll) excludes Red
  Bird and the four crafting skills; those have separate books. Original learning and upgrade
  conditions still apply. Catalog presence does not imply ordinary loot access.

ORIGINAL ICONS AND APPLICATION ICON

Skill and equipment icons are decoded at runtime from your installed game
resource libraries. Animated resources use their first frame for the preview;
their original resolution is retained. Unknown resources use a fallback.
No original game icons are distributed with this package.

The editor's own bronze-and-jade application icon was newly generated with the
built-in image generation tool. Its PNG, six-size Windows ICO and exact prompt
are included with the source assets. It is not the game's original logo.

GAME DATA TXT EDITOR

The Data editor provides a table, original Chinese field/group explanations
and an Original TXT view. It reads files directly inside the selected game's
Data folder. Unknown table structures remain available in original-text mode.

These are installation-wide definitions: a saved TXT change can affect every
save loaded by this installation. This is separate from editing a single slot.
Close SOE.EXE before saving or restoring Data text. Save first backs up the
current file, then replaces it atomically. Restore verifies the selected backup
and first creates another backup of the current contents.

The original GBK encoding, marker/XOR format, empty columns, comments and line
endings are retained. Viewing the original text or switching tabs does not save
or rewrite it. Headers, comments and unrecognized lines remain read only in the
table; use Original TXT for deliberate structural edits. The editor cannot
infer safe gameplay values for every arbitrary table change.

Data-file backups are stored under the game's SealSaveEditor-DataBackups folder.
They are separate from the save-slot backups described below.

SAVE, UNDO AND RECOVERY

Save with backup writes the slot from the working draft. Undo/Redo applies to
supported unsaved edits, including structural item/skill/companion operations.
Export copy writes an edited slot to another location with its companion files.

Save-slot backups are stored next to the GAM files in SealSaveEditor-backups.
The editor stages paired GAM/USE updates and rolls back changed files if the
transaction fails. Restore recovers the complete recorded slot, including
companion-file presence/absence, while preserving unrelated slots and files.
Stale fingerprints block overwriting files changed since they were opened.

The game may recalculate derived values and descriptions after loading. Editing
a tooltip string alone does not change an actual bonus. Coordinates apply to
the current map; cross-map relocation remains read only.

CONTENTS AND BUILD

The package contains the editor, two helper programs, this documentation,
checksums and the editor's own source/assets. It contains no original game
executables, original DLLs, decoded game tables, original icons or saved games.
Native generation requires the verified installed Steam ERRelive.dll build.
Unsupported engine/save formats are rejected rather than guessed.

To rebuild the three programs from the included source on Windows:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1

Build output is placed in source\build unless another output directory is
specified. Keep all three rebuilt EXEs together. No third-party build packages
are downloaded by the build script.

SHA256SUMS.txt records the packaged files. VALIDATION.txt distinguishes native
generation, file/GUI checks and actual in-game observations.

Project: https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch
