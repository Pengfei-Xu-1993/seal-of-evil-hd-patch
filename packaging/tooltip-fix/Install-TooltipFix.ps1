param([string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$expectedHash = '64465DCD2D60CBE617EB3D0C2133264584D9EA882705D824C36EDB870E91A34B'
$payload = Join-Path $PSScriptRoot 'payload/dinput8.dll'
if (!(Test-Path -LiteralPath $payload -PathType Leaf) -or
    (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The update payload is missing or damaged. Extract the complete package again.'
}

if (!$GameDirectory) {
    $steamRoots = @('D:\Steam', (Join-Path ${env:ProgramFiles(x86)} 'Steam'))
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        if (Test-Path -LiteralPath $key) {
            $settings = Get-ItemProperty -LiteralPath $key
            if ($settings.SteamPath) { $steamRoots += $settings.SteamPath }
            if ($settings.InstallPath) { $steamRoots += $settings.InstallPath }
        }
    }
    $libraries = @($steamRoots)
    foreach ($root in @($steamRoots | Select-Object -Unique)) {
        $vdf = Join-Path $root 'steamapps/libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $content = [IO.File]::ReadAllText($vdf)
            foreach ($match in [regex]::Matches($content, '"path"\s+"([^"]+)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    $gameCandidates = @(foreach ($library in @($libraries | Select-Object -Unique)) {
        $candidate = Join-Path $library 'steamapps/common/Seal of Evil'
        if (Test-Path -LiteralPath (Join-Path $candidate 'SOE.EXE')) {
            (Resolve-Path -LiteralPath $candidate).Path
        }
    }) | Sort-Object -Unique
    if (@($gameCandidates).Count -ne 1) {
        throw 'Could not select one game installation. Run with -GameDirectory followed by your Seal of Evil folder.'
    }
    $GameDirectory = @($gameCandidates)[0]
}
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
foreach ($required in @('SOE.EXE', 'SealHD.ini', 'D3D8.dll', 'dinput8.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $GameDirectory $required) -PathType Leaf)) {
        throw ('This update requires an existing SealHD installation. Missing: ' + $required)
    }
}
$target = Join-Path $GameDirectory 'dinput8.dll'
$oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
if ($oldHash -eq $expectedHash) {
    Write-Host 'The tooltip canvas fix is already installed.'
    return
}
$oldContents = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($target))
if (!$oldContents.Contains('SealHD proxy')) {
    throw 'The installed dinput8.dll is not a recognized SealHD proxy. It has not been replaced.'
}
foreach ($process in @(Get-Process | Where-Object { $_.ProcessName -like 'SOE*' })) {
    try { $processPath = $process.Path } catch { $processPath = $null }
    if ($processPath -and (Split-Path -Parent $processPath) -ieq $GameDirectory) {
        throw 'Close Seal of Evil before installing the update.'
    }
}

$backupDirectory = Join-Path $GameDirectory ('SealHD-tooltip-fix-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$stage = Join-Path $GameDirectory ('dinput8-tooltip-' + [Guid]::NewGuid().ToString('N') + '.tmp')
$backup = Join-Path $backupDirectory 'dinput8.dll'
try {
    Copy-Item -LiteralPath $payload -Destination $stage
    if ((Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'The staged DLL checksum does not match.'
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $oldHash) {
        throw 'The installed DLL changed during installation. No replacement was made.'
    }
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    [IO.File]::Replace($stage, $target, $backup, $true)
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expectedHash -or
        (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $oldHash) {
        throw 'Post-install verification failed. Keep the backup and inspect the installation.'
    }
    Write-Host 'Installed the tooltip canvas fix.'
    Write-Host ('Game:   ' + $GameDirectory)
    Write-Host ('Backup: ' + $backup)
    Write-Host 'Your resolution settings, artwork, wrapper configuration and saves were retained.'
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Force }
}
