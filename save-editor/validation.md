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

## Original-table item preset and requirements

The bulk-action backend suite passed **3,888 assertions**. It checked all 29 persistent effect limits and source rules, four excluded consumable actions, preservation of special/elemental/growing/disabled/conditional records, existing higher values and duplicate records, save/reload idempotence, four requirements, description-cache invalidation, foreign-document rejection, 16 KiB serialization-overflow rollback and byte-identical undo.

The maxima are the greatest declared positive `MaximumValue` annotations for each supported effect in the installed tables. They are not engine-wide hard caps: some native generation paths do not enforce that column. The preset retains existing higher values.

Requirements are updated in both current and baseline fields: `LevelReq`, `AttribReq`, `LevelReq0` and `AttribReq0`. The original engine copies between those current and baseline pairs, so changing all four prevents the saved baseline from immediately restoring the old requirements.

The final actual WinForms bulk-action run passed **120 assertions** on a separate private desktop. It exercised real buttons and menu entries, both command handlers, one-action Undo/Redo, no-op history, hidden/tab/modal guards, Save with backup, independent reload and minimum-width Chinese/English button layouts. A native modal-disabled-window check protects commands even when the managed `Enabled` property remains true.

Shortcut proof invoked the actual `ProcessCmdKey` handler through reflection. It verifies command mapping and effects, not physical keyboard delivery. The input desktop was never switched.

An independent parser validated all 280 objects in the resulting save. Exactly one item payload changed; the other 279 payloads and 278 unrelated whole subtrees were byte-identical. All four existing modifier records were preserved. Twenty-eight neutral bonuses were appended, covering 29 supported types together with an existing above-cap movement bonus. All four requirements became 1; all other item integers and noncache strings were unchanged. GAM and USE metadata were unchanged, and the seven-file backup contained byte-identical pre-action GAM and USE files. Installed source saves remained unchanged.

## Running-game observations

Separate isolated-game checks loaded files written by the real editor GUI. Observed results included changed money and skill rank, a template-generated Demonic Flame with the expected regenerated tooltip, and independently added iron ore and a hidden helm in the selected characters' bags. These checks establish loading, inventory display and the demonstrated tooltip values. They do not establish every template's combat effects, all equipment restrictions or every save/reload cycle.

The earlier table-preset checks above establish editor behavior and serialized persistence. Character, skill and conditional values can still be recalculated by the game. The current extreme-preset runtime status is recorded separately below.

The separately supplied HD tooltip-canvas update corrected the reproduced high-resolution lower-HUD tooltip corruption and multi-second frame-submission stalls. Controlled 1080p, 1440p and 4K tests verified canvas geometry and visible tooltip results. Those were CPU call/Present timestamps and screenshot checks, not physical display latency or a general performance guarantee. The tested 4K renderer still had approximately 100 ms frame intervals. The save editor does not install that renderer update.

## Extreme preset: native and arithmetic evidence

The extreme preset was derived from the supported original engine and client consumers, rather than treating generation-table annotations as global limits. [The values document](extreme-values.md) records the selected numbers, units, effective caps, operating assumptions, binary hashes and relevant function RVAs without redistributing original game data.

The corrected core review passed **11 engine-instruction guards, two client-resource snapshot guards, 19 budget checks and six resource-boundary regression checks**. These covered seven-slot resource/regeneration and kill-restoration products, signed-16 maximum-resource/rating paths, fury gain, five defenses, fixed reduction and a combined attack/skill/element/critical/frozen/impact/world/reflection budget. They verified that the modeled signed-32 intermediates stayed within range under the stated assumptions.

Actual clone testing rejected an earlier +65,536 HP/energy candidate: the character panel showed maxima of 240/100 for edited totals of 65,776/65,636. Subsequent inspection found separate packed character-info snapshot reads that sign-extend 16-bit maxima, despite a different actor-update packet carrying full DWORD HP. The final resource bonus is +4,096 per item, allowing seven copies plus up to 4,095 other maximum-resource points without crossing 32,767. The failed candidate and signed-boundary examples are retained as regressions, not reported as working values. Existing above-profile bonuses are intentionally preserved, so retesting starts from a fresh fixture.

Consumer analysis also distinguished actual percentage clamps and refill saturation from operational choices. The operational values do not establish a global maximum or guarantee arbitrary prior cheats, duplicated stronger effects, edited skills/enemies or two modified PvP opponents. Existing stronger and special records are intentionally preserved. Speeds may be very fast.

The complementary probability/speed model passed **16 arithmetic checks**, covering chance saturation, the 50% absolute-evasion clamp, signed speed/rating transport, movement/timing math, status duration/resistance products and a reflection-overflow counterexample. The frozen profile classifies six entries as effective limits and 23 as operational choices, with no original-table fallback. Chance coefficients are identified separately from final probabilities.

These are static native-instruction and arithmetic results. They do not establish actual editor interaction, successful game loading, equipment activation, visible character totals or combat outcomes.

### Current extreme-preset verification status

- **Backend/save checks:** the corrected final preset passed 315 backend assertions. Independent binary parsing checked all 280 objects in its output: only the selected item changed; the other 279 payloads and 278 unrelated subtrees were byte-identical. Original native modifiers were retained, all 29 neutral values matched the preset, requirements were 1, and complete Undo restored the original GAM/USE bytes. Earlier original-table results above remain separate.
- **Actual GUI and read-only value comparison:** the final corrected-source WinForms run passed 203 assertions on a separate desktop. It exercised the real menus, all three command handlers, the 29-row read-only comparison, modal/hidden/tab guards, complete Undo/Redo, idempotence, actual Save with backup and independent reopening. Keyboard handlers were dispatched through reflection; physical keyboard delivery was not simulated.
- **Running-game load/equipped character check:** a fresh fixture written by that final GUI run was loaded in the isolated game. Liang Hu's already-equipped Paw displayed maximum health **4,336**, maximum energy **4,196**, attack **4,134**, all five defenses **32,770**, accuracy **2,113** and evasion **2,086**. The resource maxima were readable and positive, with no earlier 16-bit wrap. Movement using the unchanged strong speed values was also exercised in a separate isolated run. The game was closed normally. These observations establish the demonstrated load, equipment activation, displayed totals and movement, rather than every possible effect or frame-rate claim.
- **Combat coverage:** no complete all-29-effect combat test is claimed.

The tested source uses the corrected +4,096 resource values. Earlier +65,536 candidate captures are failure evidence and were not used as a final acceptance pass.

## Remaining boundaries

- Verified Steam version-30 EASYRPG/USER format only; unsupported formats are rejected.
- Native item generation requires the verified installed engine build.
- Cross-map relocation and unverified structured fields remain read-only.
- Editing a cached description is separate from changing an actual effect.
- Broader runtime, hardware and complete all-effect combat coverage remain outside the evidence above.
