param(
    [string]$PayloadZip = (Join-Path $PSScriptRoot 'build/SealHD.Payload.zip'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'build/SealHDSetup.exe'),
    [string]$IconPath = (Join-Path $PSScriptRoot 'assets/SealHD.ico'),
    [switch]$AsInvokerForTesting
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
foreach ($inputPath in @($compiler, $PayloadZip, $IconPath, (Join-Path $PSScriptRoot 'installer.manifest'))) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Build input is missing: $inputPath" }
}
$payload = (Resolve-Path -LiteralPath $PayloadZip).Path
$icon = (Resolve-Path -LiteralPath $IconPath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$manifest = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'installer.manifest')).Path
$temporaryManifest = $null
try {
    if ($AsInvokerForTesting) {
        $temporaryManifest = Join-Path ([IO.Path]::GetDirectoryName($output)) ('test-only-' + [Guid]::NewGuid().ToString('N') + '.manifest')
        [IO.File]::WriteAllText($temporaryManifest, [IO.File]::ReadAllText($manifest).Replace('level="requireAdministrator"', 'level="asInvoker"'), (New-Object Text.UTF8Encoding($false)))
        $manifest = $temporaryManifest
    }
    $source = (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File).FullName
    $arguments = @('/nologo', '/target:winexe', '/platform:x86', '/optimize+', '/main:SealHdInstaller.Program',
        "/out:$output", "/win32manifest:$manifest", "/win32icon:$icon", "/resource:$payload,SealHD.Payload.zip",
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
        '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Web.Extensions.dll')
    & $compiler @arguments @source
    if ($LASTEXITCODE -ne 0) { throw "The installer compilation failed ($LASTEXITCODE)." }
    $builtFile = Get-Item -LiteralPath $output
    [PSCustomObject]@{ FullName = $builtFile.FullName; Length = $builtFile.Length; Sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash; Elevation = $(if ($AsInvokerForTesting) { 'asInvoker-test-only' } else { 'requireAdministrator' }) }
}
finally {
    if ($temporaryManifest -and (Test-Path -LiteralPath $temporaryManifest)) { Remove-Item -LiteralPath $temporaryManifest -Force }
}
