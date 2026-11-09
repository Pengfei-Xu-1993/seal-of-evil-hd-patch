# Complete graphical installer source

The installer deploys one embedded ZIP containing the native patch, graphics wrappers, configuration, five HD artwork files, and an app-local x86 video probe. Installation and restoration run directly in C#. The PowerShell commands below are developer build steps; end users only run `SealHDSetup.exe`.

## Build inputs

- Windows with the .NET Framework compiler and .NET Framework 4.6 or later reference/runtime support.
- Visual Studio 2022 C++ x86 build tools. `source/build.cmd` uses the standard VS 2022 Build Tools path; adjust it for a different installation.
- The `payload` directory extracted from the [v0.24 full ZIP](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/download/v0.24/SealHD_v0.24.zip), used only as verified build input for unchanged third-party wrappers, artwork, and default INI files. Its ZIP SHA256 is `86EC3BB96BDAAC81CCB0A1243D94D04E247F912925EE436AABC93FA31B6D403E`. You do not need to install that older package.

## Build

From the repository root:

```powershell
& .\config\Generate-ResolutionProfiles.ps1 -Check
& .\source\build.cmd
& .\installer\Build-VideoProbe.ps1
& .\installer\Build-Payload.ps1 -BasePayloadDirectory 'C:\BuildInputs\SealHD_v0.24\payload'
& .\installer\Build-Installer.ps1
```

The result is `installer/build/SealHDSetup.exe`. The normal installer requests administrator rights. `-AsInvokerForTesting` is reserved for isolated development tests and cannot handle protected HKLM settings without elevation.

`config/resolutions.json` supplies both generated resolution catalogs and the wrapper's extra mode list. Run the generator without `-Check` after changing that catalog, then commit its outputs. `Build-Payload.ps1` uses the expanded repository `config/dgVoodoo.conf` while checking unchanged inputs against the recorded payload.

The x86 GUI-subsystem video helper only builds a DirectShow graph; it does not play movies. Unsupported IV50 opening/ending playback is skipped, preserving the files. No decoder is installed.

## Released binary and source correspondence

The v0.26 release publishes the validated complete installer unchanged, renamed from `SealHDSetup-Complete-Animation-Test.exe` to `SealHDSetup.exe`:

| Artifact | SHA256 |
| --- | --- |
| `SealHDSetup.exe` | `E41845600EC704AEBBF90BF311B9C0E31EF38C21BE2971BA28AF8A63D906F9F4` |
| Embedded `dinput8.dll` | `0CF6800218B921B1949C27F689D9F3B2DFF483A4F1AAC28691A619AF06564E03` |
| Embedded payload ZIP | `AC7BEF898C4B6CA5462A6B2104779E6716AF0B0575EB44978AF0BD541197D1A5` |
| Embedded `SealHD-bin/SealVideoProbe.exe` | `ADD8E128D76CAB0B76881AE5C667BC7535F8C09F0FF3C85182166D982F96E59F` |

`tested-payload-manifest.json` records every embedded payload file. The release includes the 18 shared resolution profiles and original-frame-driven fire, steam, curtain movement, and local lighting.

The native log retains an internal `0.25-local` identifier and the payload retains `0.26-resolution-candidate`. These are the identifiers of the tested artifacts: the EXE was not rebuilt merely to change its release label. The matching implementation is included in the v0.26 source tag. Compiler timestamps, toolchain versions, and ZIP metadata can change rebuilt hashes, so the build instructions do not claim bit-for-bit reproducibility.

The earlier beta's original matching source remains available at tag `v0.25-beta.1`.

## Validation sources and scope

`validation/BackendTests.cs`, `validation/BackendDriver.cs`, and `validation/RealBackendGuiTests.cs` preserve developer test sources. Use copies of supported original game files and disposable output directories. The GUI harness requires its private-desktop launcher; these tests are not normal installation entry points.

See [release validation](../docs/resolution-scene-candidate.md) for actual game checks, installer fixtures, pixel analysis, and remaining limits.
