Seal of Evil HD Patch - tooltip canvas fix

Fixes corrupted/striped skill-tooltip backgrounds and multi-second freezes when
hovering over icons near the lower part of a high-resolution game viewport.

The old HD patch enlarged the game viewport but left two tooltip-background
initialization paths at 1024x768. This update makes both background canvases
match the selected render size. The original translucent background is retained.
It does not force an opaque tooltip, change pixel formats or use compatibility mode.

INSTALL

This is an update for an existing Seal of Evil HD Patch installation. It includes
the updated proxy and its source, not the original full artwork/wrapper package.

1. Close Seal of Evil.
2. Double-click Install.cmd. It detects the Steam game folder and backs up the
   previous HD proxy before replacing only dinput8.dll.
3. Start the game normally through Steam.

For a different game folder:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-TooltipFix.ps1 -GameDirectory "D:\Steam\steamapps\common\Seal of Evil"

Resolution settings, HD artwork, dgVoodoo configuration and saved games are kept.
The installer reports the backup location; restoring its dinput8.dll reverts this
update. The previous proxy must be a recognized SealHD proxy; another loader is
not silently replaced. The full HD patch is required if no previous patch exists.

VERIFICATION

The reported lower-HUD skill-tooltip corruption was reproduced. In the broken
1440p path, moving onto the tooltip caused about 4.7 seconds without a submitted
frame while the pointer remained there. With the canvas correction, the same
two hovers submitted their next frames after about 39 ms, with correct colors.

Instrumented 1080p, 1440p and 4K tests confirmed matching source/scratch geometry,
valid source rectangles, completed background copies and intact transparency.
These are CPU call/Present timestamps and screenshot checks, not physical display
latency or a guarantee of game FPS. On this machine the tested 4K renderer itself
had roughly 100 ms frame intervals both away from and over a tooltip; the separate
multi-second tooltip fault was eliminated.

The production DLL has no diagnostic drawing hooks, trace recorder or opaque-mode
experiment. Its functional change is four validated dimension operands for the
native tooltip canvas's initial creation and resolution-reset paths.

Developed and implemented with OpenAI Codex. The user reviewed the work.
No original game binary, saved game or game data table is included.

SOURCE

source\build.cmd builds the x86 proxy with Visual Studio 2022 C++ Build Tools.
Adjust the compiler-environment path if Build Tools are installed elsewhere.
Existing game/HD artwork is loaded from the user's installation at runtime.
