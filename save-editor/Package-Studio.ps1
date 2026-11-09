param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'build'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../../outputs'),
    [switch]$Rebuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# This script affects only the new Studio package. The old editor/HD archives are never opened.
$packageName = 'SealSaveEditor-Studio'
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputRoot)
$build = [IO.Path]::GetFullPath($BuildDirectory)
$docs = Join-Path $sourceRoot 'studio-docs'
$readme = Join-Path $docs 'README.txt'
$validation = Join-Path $docs 'VALIDATION.txt'
foreach ($path in @($readme,$validation)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing Studio document: ' + $path) }
    $text = [IO.File]::ReadAllText($path)
    if ($text -match '[^\x00-\x7F]') { throw ('Studio public documentation must be English ASCII: ' + $path) }
}
if ([IO.File]::ReadAllText($validation).Contains('PENDING_RUNTIME_RESULTS')) {
    throw 'Studio runtime validation is still a draft. Record the actual results and remove PENDING_RUNTIME_RESULTS before packaging.'
}
if ($Rebuild) {
    & (Join-Path $sourceRoot 'build.ps1') -OutputDirectory $build
    if ($LASTEXITCODE -ne 0) { throw 'Studio rebuild failed.' }
}

$inputs = [System.Collections.Generic.List[object]]::new()
function Add-PackageFile([string]$Source,[string]$Relative) {
    $full = [IO.Path]::GetFullPath($Source)
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Missing package input: ' + $full) }
    if ((Get-Item -LiteralPath $full).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Linked package input rejected: ' + $full) }
    if ($Relative.StartsWith('/') -or $Relative.Contains('..') -or $Relative.Contains(':')) { throw 'Invalid package-relative path.' }
    $inputs.Add([pscustomobject]@{Source=$full;Relative=$Relative.Replace('\','/');Sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()})
}
foreach ($name in @('SealSaveEditor.exe','SealItemWorker.exe','SealActorWorker.exe')) { Add-PackageFile (Join-Path $build $name) $name }
Add-PackageFile $readme 'README.txt'
Add-PackageFile $validation 'VALIDATION.txt'
Add-PackageFile (Join-Path $sourceRoot 'build.ps1') 'source/build.ps1'
Add-PackageFile (Join-Path $sourceRoot 'editor.manifest') 'source/editor.manifest'
Add-PackageFile (Join-Path $sourceRoot 'Package-Studio.ps1') 'source/Package-Studio.ps1'
Add-PackageFile $readme 'source/studio-docs/README.txt'
Add-PackageFile $validation 'source/studio-docs/VALIDATION.txt'
$src = Join-Path $sourceRoot 'src'
if ((Get-Item -LiteralPath $src).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked source directories are not packaged.' }
foreach ($directory in Get-ChildItem -LiteralPath $src -Directory -Recurse) {
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Linked source directory rejected: ' + $directory.FullName) }
}
foreach ($file in Get-ChildItem -LiteralPath $src -File -Recurse | Sort-Object FullName) {
    if ($file.Extension -ne '.cs') { throw ('Unreviewed non-C# source input: ' + $file.FullName) }
    $relative = $file.FullName.Substring($src.Length + 1).Replace('\','/')
    Add-PackageFile $file.FullName ('source/src/' + $relative)
}
foreach ($name in @('NativeItemWorker.cs','NativeActorWorker.cs','NativeCompanionSupport.cs')) {
    Add-PackageFile (Join-Path $sourceRoot ('native/' + $name)) ('source/native/' + $name)
}
foreach ($name in @('seal-editor-icon-ai.ico','seal-editor-icon-ai.png','ICON-PROMPT.txt','Convert-EditorIcon.ps1','ICON-NOTES.txt')) {
    Add-PackageFile (Join-Path $sourceRoot ('assets/' + $name)) ('source/assets/' + $name)
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $output ('.' + $packageName + '-stage-' + $runId)
$stage = Join-Path $stageRoot $packageName
$temporaryZip = Join-Path $stageRoot ($packageName + '.zip')
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$expected = @{}
foreach ($entry in $inputs) {
    if ($expected.ContainsKey($entry.Relative)) { throw ('Duplicate package path: ' + $entry.Relative) }
    $target = Join-Path $stage $entry.Relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $target
    $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Sha256) { throw ('Source changed during package copy: ' + $entry.Source) }
    $expected[$entry.Relative] = $entry.Sha256
}
$lines = @($expected.Keys | Sort-Object | ForEach-Object { $expected[$_] + '  ' + $_ })
$manifest = Join-Path $stage 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($manifest,$lines,[Text.UTF8Encoding]::new($false))
$expected['SHA256SUMS.txt'] = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$temporaryZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
$seen = @{}
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $relative = $entry.FullName.Replace('\','/')
        if (!$expected.ContainsKey($relative) -or $seen.ContainsKey($relative)) { throw ('Unexpected/duplicate archive file: ' + $relative) }
        $stream = $entry.Open(); $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
        finally { $algorithm.Dispose(); $stream.Dispose() }
        if ($hash -ne $expected[$relative]) { throw ('Archived content differs: ' + $relative) }
        $seen[$relative] = $true
    }
    if ($seen.Count -ne $expected.Count) { throw 'The archive is missing expected files.' }
}
finally { $archive.Dispose() }
foreach ($entry in $inputs) {
    if ((Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Sha256) {
        throw ('An input changed before publication; the existing Studio output was not replaced: ' + $entry.Source)
    }
}

$finalDirectory = Join-Path $output $packageName
$finalZip = Join-Path $output ($packageName + '.zip')
$previousDirectory = Join-Path $output ('.' + $packageName + '-previous-' + $runId)
$previousZip = Join-Path $output ('.' + $packageName + '-previous-' + $runId + '.zip')
$oldDirectoryMoved = $false; $oldZipMoved = $false; $newDirectoryMoved = $false; $newZipMoved = $false
function Assert-DirectChild([string]$Candidate,[string]$Parent,[string]$ExpectedLeaf) {
    $full = [IO.Path]::GetFullPath($Candidate)
    $withinParent = [string]::Equals([IO.Path]::GetDirectoryName($full),[IO.Path]::GetFullPath($Parent).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)
    $expectedName = [string]::Equals([IO.Path]::GetFileName($full),$ExpectedLeaf,[StringComparison]::Ordinal)
    if (!$withinParent -or !$expectedName) {
        throw ('Package move target escaped its intended directory: ' + $full)
    }
}
# Verify resolved absolute source/destination paths before any directory move.
Assert-DirectChild $finalDirectory $output $packageName
Assert-DirectChild $finalZip $output ($packageName + '.zip')
Assert-DirectChild $previousDirectory $output ('.' + $packageName + '-previous-' + $runId)
Assert-DirectChild $previousZip $output ('.' + $packageName + '-previous-' + $runId + '.zip')
Assert-DirectChild $stageRoot $output ('.' + $packageName + '-stage-' + $runId)
Assert-DirectChild $stage $stageRoot $packageName
Assert-DirectChild $temporaryZip $stageRoot ($packageName + '.zip')
try {
    if (Test-Path -LiteralPath $finalDirectory) {
        if ((Get-Item -LiteralPath $finalDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The existing Studio directory is linked; refusing replacement.' }
        [IO.Directory]::Move($finalDirectory,$previousDirectory); $oldDirectoryMoved = $true
    }
    if (Test-Path -LiteralPath $finalZip) {
        if ((Get-Item -LiteralPath $finalZip).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The existing Studio ZIP is linked; refusing replacement.' }
        [IO.File]::Move($finalZip,$previousZip); $oldZipMoved = $true
    }
    [IO.Directory]::Move($stage,$finalDirectory); $newDirectoryMoved = $true
    [IO.File]::Move($temporaryZip,$finalZip); $newZipMoved = $true
}
catch {
    # Reversible moves only. No existing package is recursively deleted.
    if ($newZipMoved) { [IO.File]::Move($finalZip,$temporaryZip) }
    if ($newDirectoryMoved) { [IO.Directory]::Move($finalDirectory,$stage) }
    if ($oldZipMoved) { [IO.File]::Move($previousZip,$finalZip) }
    if ($oldDirectoryMoved) { [IO.Directory]::Move($previousDirectory,$finalDirectory) }
    throw
}
# The staging root is now empty. Previous Studio outputs, if any, remain as owned recovery snapshots.
[IO.Directory]::Delete($stageRoot,$false)
$report = [ordered]@{
    package=$finalDirectory; archive=$finalZip; files=$expected.Count
    archiveBytes=(Get-Item -LiteralPath $finalZip).Length
    archiveSha256=(Get-FileHash -LiteralPath $finalZip -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceFingerprintsStable=$true; archiveEntryChecksumsVerified=$true
    previousPackage=if($oldDirectoryMoved){$previousDirectory}else{$null}
    previousArchive=if($oldZipMoved){$previousZip}else{$null}
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output ($packageName + '-package-report.json')) -Encoding UTF8
$report | ConvertTo-Json -Depth 4
