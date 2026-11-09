[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'native/SealVideoProbe.exe')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The .NET Framework 4 C# compiler is required.' }
$source = Join-Path $PSScriptRoot 'native/SealVideoProbe.cs'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Build input is missing: $source" }
$source = (Resolve-Path -LiteralPath $source).Path
$output = [IO.Path]::GetFullPath($OutputPath)
if ([string]::Equals($source, $output, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputPath must not overwrite the source file.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$temporary = Join-Path ([IO.Path]::GetDirectoryName($output)) ('SealVideoProbe-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler '/nologo' '/target:winexe' '/platform:x86' '/optimize+' "/out:$temporary" $source
    if ($LASTEXITCODE -ne 0) { throw "Video-probe compilation failed ($LASTEXITCODE)." }
    if ([IO.File]::Exists($output)) { [IO.File]::Replace($temporary, $output, $null) }
    else { [IO.File]::Move($temporary, $output) }
    [PSCustomObject]@{ FullName = $output; Length = (Get-Item -LiteralPath $output).Length; Sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash }
}
finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
