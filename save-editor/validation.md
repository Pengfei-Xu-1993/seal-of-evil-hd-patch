SEAL OF EVIL SAVE EDITOR STUDIO - VALIDATION RECORD

Runtime and GUI checks were completed on Windows using isolated save copies.
See the scope and remaining limitations below. Full-story playthrough and all
possible equipment/skill combinations are not claimed.

The editor and tests were developed by OpenAI Codex; the publisher reviews the
work. Local research records referenced below are not included in the package,
because they can contain original game definitions or private test saves.

SUPPORTED INPUTS AND STORAGE

- Verified original Steam ERRelive.dll SHA256:
  B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F
- EASYRPG/USER save format 30. Native helpers run in isolated x86 processes;
  their original game DLL is read from the user's installation, not bundled.
- Existing paired-save safety includes complete slot backups, GAM/USE staging,
  rollback, stale-source checks and preserving unrelated slots. Final Studio
  integration/regression results should be recorded in the runtime section.

ORDINARY ITEMS AND REAL SKILL BOOKS

- The book-enabled production worker reproduced all 2,628 prior ordinary item
  plans exactly across generation inputs 1, 14, 60 and 200.
- All 218 concrete registered native books were constructed. Exact paths,
  aliases, names, 256 integer attributes, strings and remaining base payload
  matched an independent native probe. All native use-callback skill and
  enhancement bindings were compared. Seeded replay was byte identical.
- Six malformed/unsafe requests were rejected, including an arbitrary script
  path, an unregistered book, an invalid book level and a forbidden System
  directory. Simulated abnormal worker exit, corrupt output and the 15-second
  timeout were rejected; owned worker files/processes were cleaned up.
- Book factory/save-copy suite: 68 assertions. Eleven books covering all five
  manuals, learning/upgrade/stage books, crafting and Red Bird were added,
  undone/redone and saved/reparsed. An independent parser confirmed exact native
  identities/defaults, valid framing, no book-derived tail or children, all 280
  original nodes preserved and an unchanged original GAM payload.
- Book browser: 16 actual WinForms assertions on a private desktop, including
  five continuous additions, recipient selection, automatic preview, filtering,
  real icons, stale-result rejection, pending double-click handling and dropping
  results after close. Added items were saved/reparsed only in owned copies.

These checks establish native construction and save transport. They do not
independently prove every book's right-click effect in the running game.

GAME DATA TEXT

- Actual .NET Framework backend suite: 121 assertions. All 30 installed Data
  text files were read-only round-tripped byte identically. All 27 TSV tables
  were recognized; the three headerless experience lists use original-text mode.
- Checked marker/XOR/GBK preservation, trailing empty columns and spaces,
  comments/unknown lines, mixed line endings, no final newline, strict character
  encoding, atomic save, fingerprinted backup/restore, recovery backup, tampered
  backup rejection, stale checks before/after backup and actual long paths.
- Game-already-running and game-started-after-backup branches used a private
  fixture process probe. No real SOE process was present during that backend run;
  the actual production process-discovery gate was not claimed as tested there.
- Data editor: 16 actual WinForms assertions on a private desktop, including
  real cell editing, Save/Restore buttons, backup discovery, raw/table syncing,
  no changes from viewing tabs and preserved mixed line endings after a raw edit.
  Every write used a newly owned temporary fixture, not the original installation.

ORIGINAL ICONS AND NEW APPLICATION ART

- Runtime-only resource decoding succeeded for all 684 distinct icon IDs
  referenced by the installed item-picture, skill-role and skill-enhancement
  tables. No renderer, game DLL or game window was used by the icon loader.
- Cache clones are independently owned; unsupported IDs return a fallback.
  Resource wrappers and animated icons resolve to the first native bitmap frame.
- The new application icon was generated with the built-in image generation
  tool. The saved source is a 1254 x 1254 RGBA PNG with real transparency.
  Its standard ICO contains 16/32/48/64/128/256-pixel 32-bit frames with alpha.

RUNTIME AND FINAL INTEGRATION

- Actual MainForm controls passed 41 assertions under an Application.Run
  message loop on a private desktop: Learn, Max, Undo, Redo, footer Save and
  reopen; campaign eligibility with Undo/Redo/Save; companion generation,
  member-selector refresh and Save/reopen. All original slot files retained
  their fingerprints. An initial harness without a real message loop was
  corrected; it is not counted as a production failure.
- The item browser passed 31 actual GUI assertions: original list and preview
  icons, automatic generation, level-99 quick addition, a retained custom level,
  two consecutive additions to different recipients, the dialog remaining open,
  and safe closure while a native preview finishes. Its callback test used real
  generated objects without claiming a separate in-game load for those callbacks.
- Five-class skill transport passed 207,466 backend assertions plus 104,893
  independent parser checks, including custom-effect preservation and composed
  companion/skill history. Companion transport passed 393 backend assertions
  and 31,064 independent checks; unrelated original payloads were unchanged.
- An isolated game loaded five actual party members. Read-only native memory
  checks confirmed party positions 1..5, primary GameCount=1, 38 skills/244
  enhancement instances for Lan Wei and 21 skills/110 enhancements for Yan Hong.
  Native rank limits, 47 associated-skill bindings and ten left/right selected
  skill handles were valid. Enhancement Level placeholders remained -1.
- A separate existing-skill case learned Black Tortoise at rank 1/Timer 20,
  then applied Max and saved rank 5/Timer 20 without replacing the existing
  source parameters. Actual game loading refreshed it to rank 5/Timer 40.
- Three skill books were used individually by actual right clicks in the
  isolated game. Read-only snapshots after each click confirmed Wood Cantrip
  was absent, learned at rank 1, upgraded to rank 2, and retained at rank 2
  after Witch Scroll added other class skills. Book counts were 3,2,1,0 and
  the final skill/form count was 34. This tests these representative callbacks,
  not right-click use of every one of the 218 registered books.
- Second-playthrough conversion of the user's selected slot retained current
  progress. A complete seven-file backup was created; only one USE byte for
  primary GameCount changed. No quest completion or hidden ending was forced.
- Earlier hidden-gear, ordinary-item and extreme-preset in-game checks remain
  separate evidence. This update does not claim a new combat balance test.
- The full campaign after early companion recruitment and every possible
  activation branch have not been played through. Original story scripts can
  still recruit or replace a companion. The TXT process-running check was
  exercised with fixtures, as described above, not a real production SOE process.

FINAL PACKAGE CHECKS

Package-Studio.ps1 uses an explicit file list, captures source fingerprints,
writes SHA256SUMS.txt and verifies every ZIP entry against those fingerprints.
It refuses original game DLLs/data/icons/saves and leaves the older editor/HD
packages untouched. Per-file package fingerprints are recorded in SHA256SUMS.txt.
The separate package report records the archive verification result; an
extracted-source rebuild is checked separately before delivery.
