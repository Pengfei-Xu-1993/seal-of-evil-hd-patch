# Complete graphical installer source

The installer deploys one embedded ZIP with the native patch, graphics wrappers, configuration, five HD artwork files, and app-local x86 video probe. Installation and restoration run directly in C#; the PowerShell commands below are developer build steps, not end-user installation requirements.

## Build inputs

- Windows with the .NET Framework compiler and .NET Framework 4.6 or later reference/runtime support.
- Visual Studio 2022 C++ x86 build tools. `source/build.cmd` uses the standard VS 2022 Build Tools installation path; adjust it for a different Visual Studio installation.
- The `payload` directory extracted from the original [v0.24 full ZIP](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/download/v0.24/SealHD_v0.24.zip). Its ZIP SHA256 is `86EC3BB96BDAAC81CCB0A1243D94D04E247F912925EE436AABC93FA31B6D403E`. This supplies the unchanged third-party wrappers, configuration and artwork for a source rebuild. You do not need to install that older package.

## Build

From the repository root:

```powershell
& .\config\Generate-ResolutionProfiles.ps1 -Check
& .\source\build.cmd
& .\installer\Build-VideoProbe.ps1
& .\installer\Build-Payload.ps1 -BasePayloadDirectory 'C:\BuildInputs\SealHD_v0.24\payload'
& .\installer\Build-Installer.ps1
```

The result is `installer/build/SealHDSetup.exe`. The normal build requests administrator rights. `-AsInvokerForTesting` is reserved for isolated development tests and cannot handle protected HKLM settings without elevation.

The video helper is built as x86, GUI subsystem, using the .NET Framework compiler. It only builds a DirectShow graph; it does not play movies. The native patch uses this result to skip unsupported original opening/ending playback while preserving the files.

## Current local candidate

The working source now includes 18 shared resolution profiles and original-frame-driven fire, steam, curtain movement and local lighting. See [candidate details and validation](../docs/resolution-scene-candidate.md). `Build-Payload.ps1` uses the repository's expanded `config/dgVoodoo.conf`; unchanged wrappers, artwork and default INI inputs still come from the verified base package.

## Previously published beta correspondence

The beta publishes the previously audited EXE unchanged:

| Artifact | SHA256 |
| --- | --- |
| `SealHDSetup-Portable-Test.exe` | `0E6523E32828F63B1057F3A93A45EB4D62168A44C9BB612E8522F65CB7703032` |
| Embedded `dinput8.dll` | `0D5F939BA8F1727B4D4BCEEA17025E134C2C2325A00892D95200C1615E01EF6A` |
| Embedded `SealHD-bin/SealVideoProbe.exe` | `ADD8E128D76CAB0B76881AE5C667BC7535F8C09F0FF3C85182166D982F96E59F` |

`tested-payload-manifest.json` records the current candidate payload. The identities above describe the earlier published beta; its original matching source remains available at tag `v0.25-beta.1`. Compiler timestamps, toolchain versions and ZIP metadata can change rebuilt binary hashes, so these instructions do not claim bit-for-bit reproducibility.

The native log and payload version retain `0.25-local` / `0.25-local-portability-test` identifiers because the tested EXE was not rebuilt just to publish it as `v0.25-beta.1`.

## Validation sources

`validation/BackendTests.cs`, `validation/BackendDriver.cs`, and `validation/RealBackendGuiTests.cs` preserve the test sources. Backend fixtures must use copies of supported original game files and a disposable output directory; the GUI harness requires its private-desktop launcher. These are developer tests, not normal installation entry points. See [validation scope](../docs/portability-beta-validation.md).
