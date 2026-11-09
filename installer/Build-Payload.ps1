[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BasePayloadDirectory,
    [string]$ProxyDllPath = (Join-Path $PSScriptRoot '../source/dinput8.dll'),
    [string]$VideoProbePath = (Join-Path $PSScriptRoot 'native/SealVideoProbe.exe'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'build/SealHD.Payload.zip')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-InputFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Build input is missing: $Path" }
    return (Resolve-Path -LiteralPath $Path).Path
}

if (-not (Test-Path -LiteralPath $BasePayloadDirectory -PathType Container)) {
    throw 'BasePayloadDirectory must be the payload folder extracted from the complete v0.24 release.'
}
$base = (Resolve-Path -LiteralPath $BasePayloadDirectory).Path
$testedManifestPath = Get-InputFile (Join-Path $PSScriptRoot 'tested-payload-manifest.json')
$tested = Get-Content -LiteralPath $testedManifestPath -Raw | ConvertFrom-Json
$paths = @(
    'dinput8.dll', 'D3D8.dll', 'DDraw.dll', 'D3DImm.dll', 'dgVoodoo.conf', 'SealHD.ini',
    'SealHD-assets/intro-zoom.png', 'SealHD-assets/intro-object.png',
    'SealHD-assets/intro-strategy.png', 'SealHD-assets/mainmenu.png',
    'SealHD-assets/opening-title.png', 'SealHD-bin/SealVideoProbe.exe'
)
$inputs = [ordered]@{}
foreach ($relative in $paths) {
    $inputPath = Join-Path $base $relative
    if ($relative -eq 'dinput8.dll') { $inputPath = $ProxyDllPath }
    if ($relative -eq 'SealHD-bin/SealVideoProbe.exe') { $inputPath = $VideoProbePath }
    $inputs[$relative] = Get-InputFile $inputPath
}
$output = [IO.Path]::GetFullPath($OutputPath)
$comparisonOutput = $output
if (Test-Path -LiteralPath $output) {
    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw 'OutputPath is not a file path.' }
    $comparisonOutput = (Resolve-Path -LiteralPath $output).Path
}
foreach ($inputPath in @($inputs.Values) + @($testedManifestPath)) {
    if ([string]::Equals($comparisonOutput, $inputPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputPath must not overwrite a build input.'
    }
}

# Reused wrapper, configuration and artwork bytes must match the tested base.
# Rebuilt proxy/helper binaries intentionally receive their own fresh hashes.
foreach ($relative in $paths) {
    if ($relative -in @('dinput8.dll', 'SealHD-bin/SealVideoProbe.exe')) { continue }
    $entry = @($tested.Files | Where-Object { $_.Path -eq $relative })
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $inputs[$relative] -Algorithm SHA256).Hash -ne $entry[0].Sha256) {
        throw "The base release input does not match the tested payload: $relative"
    }
}

$outputDirectory = [IO.Path]::GetDirectoryName($output)
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$stage = Join-Path $outputDirectory ('payload-' + [Guid]::NewGuid().ToString('N'))
$temporaryZip = Join-Path $outputDirectory ('SealHD-' + [Guid]::NewGuid().ToString('N') + '.zip')
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    foreach ($relative in $paths) {
        $destination = Join-Path $stage $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($inputs[$relative], $destination, $false)
    }
    $manifest = [ordered]@{
        Version = '0.25-local-portability-test'
        Files = @($paths | ForEach-Object {
            [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $stage $_) -Algorithm SHA256).Hash }
        })
        SupportedGameHashes = [ordered]@{
            'SOE.EXE' = '452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B'
            'ERRelive.dll' = 'B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F'
            'Gfx3d.dll' = '1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078'
        }
    }
    [IO.File]::WriteAllText((Join-Path $stage 'bundle-manifest.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    if ([IO.File]::Exists($output)) { [IO.File]::Replace($temporaryZip, $output, $null) }
    else { [IO.File]::Move($temporaryZip, $output) }
    [PSCustomObject]@{ FullName = $output; Length = (Get-Item -LiteralPath $output).Length; Sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash }
}
finally {
    # The GUID staging folder is retained for inspection; never recursively delete paths.
    if ([IO.File]::Exists($temporaryZip)) { [IO.File]::Delete($temporaryZip) }
}
