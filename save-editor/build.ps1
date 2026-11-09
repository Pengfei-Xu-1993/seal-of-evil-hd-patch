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
& $compiler /nologo /target:exe /platform:x86 /optimize+ /utf8output /reference:System.dll /reference:System.Core.dll ('/out:'+$workerOutput) $workerSource
if ($LASTEXITCODE -ne 0) { throw 'Item worker compilation failed.' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/win32manifest:'+(Join-Path $PSScriptRoot 'editor.manifest')) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/out:'+$output) $sources
if ($LASTEXITCODE -ne 0) { throw 'Save editor compilation failed.' }
$output
