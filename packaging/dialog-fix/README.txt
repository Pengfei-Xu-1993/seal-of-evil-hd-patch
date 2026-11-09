Seal of Evil HD Patch - Fullscreen Confirmation Update

Fixes the game minimizing to the desktop when an owned confirmation dialog
appears, including the prompt to overwrite a saved game. The original dialog
and its confirmation choices remain available over the fullscreen game.

This update includes the previous Windows 11 compatibility, high-resolution
rendering and tooltip-canvas fixes. It requires an existing complete HD patch
installation; the existing artwork and presentation wrapper are reused.

Exit the game, extract the complete ZIP and double-click Install.cmd.
For a different game folder, run in PowerShell:

  .\Install-DialogFix.ps1 -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"

The default folder is D:\Steam\steamapps\common\Seal of Evil.
The installer backs up the previous DLL and updates an existing HD uninstall
record while retaining its original-file history. Resolution settings, artwork,
wrapper configuration and saved games are preserved.

The source folder contains the proxy source and its build command. Original
game files, artwork and third-party wrapper binaries are not included here.
Developed and implemented with OpenAI Codex; the publisher reviews the work.
