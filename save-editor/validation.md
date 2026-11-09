# Validation and evidence limits

This summary distinguishes save-format checks, actual editor interaction, native item construction and the narrower observations made in a running game. It reports completed local verification; it is not a guarantee for every game build, system or save combination.

The detailed captures, original data, native catalog dumps and save fixtures are not distributed because they contain installation-specific game material or saves. Internal research filenames are omitted here; they are not public download links.

## Save format and storage

The core suite passed 62 assertions, modifier editing passed 18, replacement transport passed 19, and native generation/replacement passed 54. Checks included byte-identical no-edit round trips, actual scalar and string changes, variable-length rebuilding, paired GAM/USE writes, backups, stale-source rejection and rollback after a forced late failure.

New-item insertion passed 60 backend assertions. An independent parser confirmed four added item objects, all 280 original object payloads unchanged, and byte-identical complete undo. Inventory placement follows the original category pages, ownership and native quantity semantics.

## Catalog and native generation

The installed catalog contains 657 ordinary item templates, 243 attribute rules and 239 attribute groups. Nested groups resolve without missing targets or cycles. Another 16 illustration/description entries are separated from ordinary generation templates. Catalog descriptions passed 5,736 checks.

The separate x86 item worker invokes the installed original item constructor and factory. It generated every alias at inputs 1, 14, 60 and 200: **2,628 successful plans**, with **60,290 independent checks**. Inputs that repeat the same seed produced identical plans in the replay cases tested. Unsupported engine hashes, invalid requests and unsafe worker workspaces were rejected.

The verified installed engine SHA256 is:

```text
B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F
```

The worker receives temporary copies of definition tables and the required name dialog, with no `System` directory or saves. A worker-only before/after comparison of 93 installed files found no additions, removals or changes to contents, lengths or timestamps. Source/import inspection found no worker registry-writing code, network access or game UI launch; this was not a registry event trace.

## Chinese display names

An independent audit passed 8,149 checks. All 657 native names, exact aliases, IDs and types remained unchanged. The 208 trusted table annotations and 449 reference-name selections had explicit provenance, including trailing-whitespace identity cases. All 20 hidden set pieces use labeled reference translations.

Actual Chinese/English GUI tests checked inventory and catalog searches, language switching without dirtying saves, and item generation without replacing native names with reference translations.

## Bulk item presets

The bulk-action backend suite passed **3,888 assertions**. It checked all 29 persistent effect limits and source rules, four excluded consumable actions, preservation of special/elemental/growing/disabled/conditional records, existing higher values and duplicate records, save/reload idempotence, four requirements, description-cache invalidation, foreign-document rejection, 16 KiB serialization-overflow rollback and byte-identical undo.

The maxima are the greatest declared positive `MaximumValue` annotations for each supported effect in the installed tables. They are not engine-wide hard caps: some native generation paths do not enforce that column. The preset retains existing higher values.

Requirements are updated in both current and baseline fields: `LevelReq`, `AttribReq`, `LevelReq0` and `AttribReq0`. The original engine copies between those current and baseline pairs, so changing all four prevents the saved baseline from immediately restoring the old requirements.

The final actual WinForms bulk-action run passed **120 assertions** on a separate private desktop. It exercised real buttons and menu entries, both command handlers, one-action Undo/Redo, no-op history, hidden/tab/modal guards, Save with backup, independent reload and minimum-width Chinese/English button layouts. A native modal-disabled-window check protects commands even when the managed `Enabled` property remains true.

Shortcut proof invoked the actual `ProcessCmdKey` handler through reflection. It verifies command mapping and effects, not physical keyboard delivery. The input desktop was never switched.

An independent parser validated all 280 objects in the resulting save. Exactly one item payload changed; the other 279 payloads and 278 unrelated whole subtrees were byte-identical. All four existing modifier records were preserved. Twenty-eight neutral bonuses were appended, covering 29 supported types together with an existing above-cap movement bonus. All four requirements became 1; all other item integers and noncache strings were unchanged. GAM and USE metadata were unchanged, and the seven-file backup contained byte-identical pre-action GAM and USE files. Installed source saves remained unchanged.

## Running-game observations

Separate isolated-game checks loaded files written by the real editor GUI. Observed results included changed money and skill rank, a template-generated Demonic Flame with the expected regenerated tooltip, and independently added iron ore and a hidden helm in the selected characters' bags. These checks establish loading, inventory display and the demonstrated tooltip values. They do not establish every template's combat effects, all equipment restrictions or every save/reload cycle.

The new bulk-bonus and minimum-requirement presets have not yet had a separate running-game equipment/combat test. Their editor behavior and serialized persistence were verified as described above. Character, skill and conditional values can still be recalculated by the game.

The separately supplied HD tooltip-canvas update corrected the reproduced high-resolution lower-HUD tooltip corruption and multi-second frame-submission stalls. Controlled 1080p, 1440p and 4K tests verified canvas geometry and visible tooltip results. Those were CPU call/Present timestamps and screenshot checks, not physical display latency or a general performance guarantee. The tested 4K renderer still had approximately 100 ms frame intervals. The save editor does not install that renderer update.

## Remaining boundaries

- Verified Steam version-30 EASYRPG/USER format only; unsupported formats are rejected.
- Native item generation requires the verified installed engine build.
- Cross-map relocation and unverified structured fields remain read-only.
- Editing a cached description is separate from changing an actual effect.
- Broader runtime, hardware and complete bulk-preset combat coverage remain outside the evidence above.
