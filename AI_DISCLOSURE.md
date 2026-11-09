# AI Development and Artwork Disclosure

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

ImageGen edited the static artwork. This document records the division of work, testing scope, and sources of existing assets.

## Publisher's review role

The publisher reviewed the work and authorized publication, including reviewing features, artwork, and test results. Codex performed the code implementation, development, builds, packaging, and test operations described below.

## Codex's contribution

Codex implemented the C++ proxy DLL, display and input adaptation, enhanced-art presentation, PowerShell installation and restoration scripts, configuration, build process, packaging, and documentation. Codex also implemented the C# save editor, paired-save storage, item catalog reader, native item-generation worker, Chinese reference-name presentation and bulk item presets. Codex operated the game for testing, examined runtime logs and process state, and checked installation, uninstall, backup, and release-file hashes. The source and scripts contain AI-written and AI-modified content.

Project records distinguish static file checks, isolated installer tests, and actual game execution. Earlier Steam testing covered main-menu startup, overlay opening and closing, returned-menu input, and exit. Direct-launch game tests covered world resolution changes, input and fullscreen menus. The tooltip-canvas correction was tested in isolated game sessions at 1080p, 1440p and 4K, followed by a production-DLL visual check at 1440p. These new world tests were direct launches, not a separate Steam-launched world acceptance test. See [HD validation](docs/validation.txt).

The save editor has separate save-format, GUI, backup, catalog, native-generation and round-trip checks. Isolated running-game checks demonstrated edited money and skill rank, a replaced hidden weapon, and added items with regenerated tooltips. The final extreme item preset passed GUI and save/reload tests and an isolated equipped-character check with the expected attack, five defenses and corrected resource maxima. An earlier resource value was rejected after a client display wrap was observed. Full combined combat behavior across all 29 effects has not been separately verified. See [save-editor validation](save-editor/validation.md).

## Complete graphical installer and scene animation

Codex implemented the graphical C# installer, app-local movie-graph probe, targeted compatibility-setting handling, and presentation-path fallback. The project owner only reviews and approves the work. The v0.26 installer is the validated complete-animation EXE renamed without recompilation; its implementation and recorded hashes are included in the release source.

Codex also implemented the shared 18-mode resolution catalog and reconstructed main-menu animation from original frame sequences in each player's installation. Five original cauldron frames drive lower and side flames and steam; nine original environment frames drive local light changes and measured curtain movement. The HD artwork stays in place, with localized effects rather than the original low-resolution scene being placed over it. This is a reconstruction, not a physical lighting simulation or a pixel-identical reproduction of the original composition.

The [current release validation](docs/resolution-scene-candidate.md) distinguishes actual game execution and recorded frames from installer fixtures and GPU readback checks. The user reported that the earlier beta worked on the other computer; the expanded-animation release has not been independently validated there. The final elevated/UAC double-click flow also remains unverified end to end. Missing IV50 support results in explicitly disclosed video skipping with original files preserved, not complete original-movie decoding. The earlier [portability audit](docs/portability-beta-validation.md) remains available as historical evidence.

## Save-editor names and game data

The editor reads definitions from the user's own installation. Its Chinese display names combine reviewed annotations with explicitly labeled reference translations; these are not claimed to reproduce an official Chinese localization. Original aliases, IDs and stored English names remain unchanged. The separate native item worker loads the verified installed engine; that engine and the original data tables are not distributed with the editor. No user saves or private reference-project files are included.

The expanded editor adds a separate native actor worker, bound skill-book generation, class skill-tree actions, companion insertion, controlled second-playthrough eligibility, game Data TXT editing and original-resource icon decoding. Tests distinguish all-book native construction from representative in-game book use and do not claim a full campaign after arbitrary edits. The fullscreen-confirmation patch was checked with actual Cancel/OK clicks and Alt+Tab, with read-only window monitoring.

The editor's new application icon was generated as a separate bronze-and-jade design with a transparent background. Its source PNG is 1254x1254; the ICO contains standard application-icon sizes. The saved prompt and conversion source are in [save-editor/assets](save-editor/assets/). Original skill/equipment icons are read from each user's installation at runtime and are not distributed.

## ImageGen and 4K exports

ImageGen used original game references and prompts to edit the main menu, opening title, and three static brand cards. The main-menu restoration target retained the original central objects and textures while limiting added objects in the widescreen side areas. The title restoration target retained the three hanging ornaments and central composition.

| File | ImageGen original size | Final PNG size |
| --- | --- | --- |
| `mainmenu.png` | 1672x941 | 3840x2160 |
| `intro-zoom.png` | 1672x941 | 3840x2160 |
| `intro-object.png` | 1672x941 | 3840x2160 |
| `intro-strategy.png` | 1672x941 | 3840x2160 |
| `opening-title.png` | 1671x941 | 3840x2160 |

High-quality bicubic resampling produced the final 4K exports from these generated originals. The prompt record is in [provenance/artwork-prompts.txt](provenance/artwork-prompts.txt); dimensions and exported asset hashes are in [provenance/artwork-manifest.json](provenance/artwork-manifest.json).

## Original game and third-party components

Original game scenes, artwork, names, and brand marks belong to their respective rights holders. The AI edits used these existing images as references, with their source recorded in the project documentation.

dgVoodoo 2.87.6 is provided by Dege. The package's x86 `D3D8.dll`, `DDraw.dll`, and `D3DImm.dll` come from the official release and are unmodified third-party components. See [THIRD_PARTY.md](THIRD_PARTY.md).

Authorization to produce and publish this patch does not make the underlying game assets, trademarks, or third-party components original work by the publisher, Codex, or ImageGen.
