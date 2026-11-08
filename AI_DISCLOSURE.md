# AI Development and Artwork Disclosure

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

ImageGen edited the static artwork. This document records the division of work, testing scope, and sources of existing assets.

## Publisher's review role

The publisher reviewed the work and authorized publication, including reviewing features, artwork, and test results. Codex performed the code implementation, development, builds, packaging, and test operations described below.

## Codex's contribution

Codex implemented the C++ proxy DLL, display and input adaptation, enhanced-art presentation, PowerShell installation and restoration scripts, configuration, build process, packaging, and documentation. Codex also operated the game for testing, examined runtime logs and process state, and checked installation, uninstall, backup, and release-file hashes. The source and scripts contain AI-written and AI-modified content.

Project records distinguish static file checks, isolated installer tests, and actual game execution. v0.22 Steam testing covered main-menu startup, overlay opening and closing, returned-menu input, and exit. World-resolution and input evidence comes from v0.21 direct-launch testing; a Steam-launched world session has not been separately retested. See [docs/validation.txt](docs/validation.txt).

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
