# Seal of Evil HD Patch v0.23

A **Windows 11 compatibility and high-resolution patch** for *Seal of Evil*,
with **1080p, 1440p (2K), 4K, borderless fullscreen, and enhanced opening and
main-menu artwork exported at 4K**.

This release also fixes corrupted skill-tooltip backgrounds and multi-second
freezes when hovering over icons at high resolutions. The tooltip background
now matches the selected render size while retaining its original transparency.

Developed and implemented with **OpenAI Codex**. The publisher only reviewed the
work and did not write the code.

## Download and install

- **New installation:** download `SealHD_v0.23.zip`, extract it completely, close
  the game and run `Install.cmd`. This includes all patch components and the five
  enhanced artwork files. Start the game normally through Steam afterward.
- **Already using the HD patch:** download `SealHD-TooltipFix.zip` to update only
  the proxy and retain your existing display settings. The full installer
  reapplies display settings and defaults to 1080p.
- For another Steam library, run `Install.cmd -GameDirectory
  "E:\SteamLibrary\steamapps\common\Seal of Evil"` from the extracted folder.
  The installer otherwise uses its preset `D:\Steam` location.
- Select 1080p, 1440p or 4K in the game's resolution options. The full package
  retains first-install backups and provides an uninstall/restore command.

The patch addresses the legacy startup display-mode failure without requiring
Windows compatibility mode or the 640x480 compatibility setting. The installer
checks the supported Steam game build before applying files.

## Checks and limits

Skill-panel and lower-HUD tooltip tests passed at 1080p, 1440p and 4K with
matching background-canvas dimensions and intact transparency. The reported
1440p lower-HUD fault went from approximately 4.7 seconds without a submitted
frame to about 39 ms for the two measured hovers. This fixes that fault; general
4K rendering performance still depends on the game and hardware.

The complete package retains the tested dgVoodoo presentation components,
installation/restore mechanism and existing artwork. Fresh installation,
upgrade, repeated installation and uninstall were checked in isolated folders.
See the package's `validation.txt` for scope and `SHA256SUMS.txt` for file hashes.

The five artwork files are 3840x2160 exports of ImageGen edits, resampled from
approximately 1672x941 generated originals. Artwork provenance and AI disclosure
are included. Original game executables, game data tables and saves are excluded.
