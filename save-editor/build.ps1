param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object {$_.FullName}
$output = Join-Path (Resolve-Path -LiteralPath $OutputDirectory) 'SealSaveEditor.exe'
$workerSource = Join-Path $PSScriptRoot 'native/NativeItemWorker.cs'
if (!(Test-Path -LiteralPath $workerSource)) { throw 'The native item worker source was not found.' }
$workerOutput = Join-Path (Resolve-Path -LiteralPath $OutputDirectory) 'SealItemWorker.exe'
& $compiler /nologo /target:exe /platform:x86 /optimize+ /utf8output /reference:System.dll /reference:System.Core.dll ('/out:'+$workerOutput) $workerSource (Join-Path $PSScriptRoot 'src/ScriptItemCatalog.cs')
if ($LASTEXITCODE -ne 0) { throw 'Item worker compilation failed.' }
$actorWorker = Join-Path (Resolve-Path -LiteralPath $OutputDirectory) 'SealActorWorker.exe'
& $compiler /nologo /target:exe /platform:x86 /optimize+ /utf8output /reference:System.dll /reference:System.Core.dll ('/out:'+$actorWorker) (Join-Path $PSScriptRoot 'native/NativeActorWorker.cs') (Join-Path $PSScriptRoot 'native/NativeCompanionSupport.cs')
if ($LASTEXITCODE -ne 0) { throw 'Actor worker compilation failed.' }
$icon = Join-Path $PSScriptRoot 'assets/seal-editor-icon-ai.ico'
if (!(Test-Path -LiteralPath $icon)) { throw 'The application icon is missing from assets.' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/win32manifest:'+(Join-Path $PSScriptRoot 'editor.manifest')) ('/win32icon:'+$icon) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/out:'+$output) $sources
if ($LASTEXITCODE -ne 0) { throw 'Save editor compilation failed.' }
$output
