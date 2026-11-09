param([string]$GameDirectory = 'D:\Steam\steamapps\common\Seal of Evil')
$ErrorActionPreference = 'Stop'
$expected = '54A78C9003CF8B373EFCA9A5C6187E360060B3DBD9AD22E5D5D1B29F90DEAB74'
$payload = Join-Path $PSScriptRoot 'payload/dinput8.dll'
if (!(Test-Path -LiteralPath $payload) -or (Get-FileHash -LiteralPath $payload).Hash -ne $expected) {
    throw 'The update payload is missing or damaged. Extract the complete package.'
}
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
foreach ($name in @('SOE.EXE','dinput8.dll','D3D8.dll','SealHD.ini')) {
    if (!(Test-Path -LiteralPath (Join-Path $GameDirectory $name) -PathType Leaf)) { throw ('This update requires an installed HD patch: missing ' + $name) }
}
foreach ($process in @(Get-Process | Where-Object {$_.ProcessName -like 'SOE*'})) {
    try { $processPath = $process.Path } catch { $processPath = $null }
    if ($processPath -and (Split-Path -Parent $processPath) -ieq $GameDirectory) { throw 'Close Seal of Evil before updating.' }
}
$target = Join-Path $GameDirectory 'dinput8.dll'
$oldHash = (Get-FileHash -LiteralPath $target).Hash
if ($oldHash -eq $expected) { Write-Output 'The fullscreen confirmation fix is already installed.'; return }
if (![Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($target)).Contains('SealHD proxy')) { throw 'The current dinput8.dll is not a recognized HD patch.' }
$statePath = Join-Path $GameDirectory 'SealHD-backup/backup.json'
$stateBytes = $null; $stateHash = $null; $newStateHash = $null
if (Test-Path -LiteralPath $statePath) {
    $stateHash = (Get-FileHash -LiteralPath $statePath).Hash
    $state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json
    $entry = @($state.Files | Where-Object {$_.RelativePath -ieq 'dinput8.dll'})
    if ($state.Version -ne 1 -or $state.GameDirectory.TrimEnd('\') -ine $GameDirectory -or $entry.Count -ne 1) { throw 'The HD backup record does not match this installation.' }
    $entry[0].InstalledSha256 = $expected
    $stateBytes = [Text.UTF8Encoding]::new($false).GetBytes(($state | ConvertTo-Json -Depth 12))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $newStateHash = [BitConverter]::ToString($sha.ComputeHash($stateBytes)).Replace('-','') } finally { $sha.Dispose() }
}
$id = [Guid]::NewGuid().ToString('N')
$backup = Join-Path $GameDirectory ('SealHD-dialog-fix-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $id.Substring(0,8))
$stage = Join-Path $GameDirectory ('dinput8-dialog-' + $id + '.tmp')
$stateStage = Join-Path $GameDirectory ('SealHD-state-' + $id + '.tmp')
$dllApplied = $false; $stateApplied = $false
try {
    Copy-Item -LiteralPath $payload -Destination $stage
    if ($stateBytes) { [IO.File]::WriteAllBytes($stateStage,$stateBytes) }
    if ((Get-FileHash -LiteralPath $stage).Hash -ne $expected -or (Get-FileHash -LiteralPath $target).Hash -ne $oldHash) { throw 'A file changed during preparation.' }
    if ($stateHash -and (Get-FileHash -LiteralPath $statePath).Hash -ne $stateHash) { throw 'The backup record changed during preparation.' }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    [IO.File]::Replace($stage,$target,(Join-Path $backup 'dinput8.dll'),$true); $dllApplied = $true
    if ($stateBytes) { [IO.File]::Replace($stateStage,$statePath,(Join-Path $backup 'backup.json'),$true); $stateApplied = $true }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $expected -or (Get-FileHash -LiteralPath (Join-Path $backup 'dinput8.dll')).Hash -ne $oldHash) { throw 'DLL verification failed.' }
    if ($stateApplied -and (Get-FileHash -LiteralPath $statePath).Hash -ne $newStateHash) { throw 'Backup-record verification failed.' }
    Write-Output 'Installed the fullscreen confirmation fix.'
    Write-Output ('Game: ' + $GameDirectory)
    Write-Output ('Previous DLL and installation record: ' + $backup)
} catch {
    if ($stateApplied -and (Get-FileHash -LiteralPath $statePath).Hash -eq $newStateHash) {
        Copy-Item -LiteralPath (Join-Path $backup 'backup.json') -Destination $stateStage
        [IO.File]::Replace($stateStage,$statePath,$null)
    }
    if ($dllApplied -and (Get-FileHash -LiteralPath $target).Hash -eq $expected) {
        Copy-Item -LiteralPath (Join-Path $backup 'dinput8.dll') -Destination $stage
        [IO.File]::Replace($stage,$target,$null)
    }
    throw
} finally {
    foreach ($temporary in @($stage,$stateStage)) { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
