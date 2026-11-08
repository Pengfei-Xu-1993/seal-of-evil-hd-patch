[CmdletBinding()]
param(
    [ValidateSet('Install', 'Resolution', 'Uninstall')]
    [string]$Action = 'Install',

    [string]$GameDirectory = 'D:\Steam\steamapps\common\Seal of Evil',

    [ValidateRange(640, 7680)]
    [int]$Width = 1920,

    [ValidateRange(480, 4320)]
    [int]$Height = 1080,

    [bool]$Borderless = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$originalPayloadNames = @(
    'dinput8.dll', 'SealHD.ini', 'D3D8.dll', 'DDraw.dll',
    'D3DImm.dll', 'dgVoodoo.conf'
)
$assetNames = @(
    'SealHD-assets\intro-zoom.png', 'SealHD-assets\intro-object.png',
    'SealHD-assets\intro-strategy.png', 'SealHD-assets\mainmenu.png',
    'SealHD-assets\opening-title.png'
)
$payloadNames = $originalPayloadNames + $assetNames
$originalAffectedNames = $originalPayloadNames + 'Ini\Config.ini'
$affectedNames = $payloadNames + 'Ini\Config.ini'
$latin1 = [Text.Encoding]::GetEncoding(28591)
$gameEncoding = [Text.Encoding]::GetEncoding(936)

function Get-BytesHash {
    param([byte[]]$Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha256.ComputeHash($Bytes)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Write-BytesAtomically {
    param([string]$Path, [byte[]]$Bytes)

    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    $temporaryPath = $Path + '.SealHD-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllBytes($temporaryPath, $Bytes)
        if ([IO.File]::Exists($Path)) {
            [IO.File]::Replace($temporaryPath, $Path, [NullString]::Value)
        }
        else {
            [IO.File]::Move($temporaryPath, $Path)
        }
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

function Set-HDDisplaySettings {
    param([byte[]]$Bytes)

    # Latin-1 keeps every existing byte intact, including non-ASCII comments.
    $text = $latin1.GetString($Bytes)
    $sectionPattern = '(?ims)^\[Display\][ \t]*\r?\n(?<body>.*?)(?=^\[|\z)'
    $sectionMatches = [regex]::Matches($text, $sectionPattern)
    if ($sectionMatches.Count -ne 1) {
        throw 'SealHD.ini must contain exactly one [Display] section.'
    }

    $section = $sectionMatches[0]
    $body = $section.Groups['body'].Value
    $settings = [ordered]@{
        ProfileIndex = $profileIndex
        Width = $Width
        Height = $Height
        Borderless = [int]$Borderless
    }
    foreach ($key in $settings.Keys) {
        $pattern = '(?im)^(?<prefix>[ \t]*' + $key + '[ \t]*=[ \t]*)\d+(?=[ \t]*(?:[;#][^\r\n]*)?\r?$)'
        if ([regex]::Matches($body, $pattern).Count -ne 1) {
            throw "SealHD.ini must contain exactly one numeric $key in [Display]."
        }
        $replacement = [Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return $match.Groups['prefix'].Value + [string]$settings[$key]
        }
        $body = [regex]::Replace($body, $pattern, $replacement)
    }
    $bodyIndex = $section.Groups['body'].Index
    $updated = $text.Substring(0, $bodyIndex) + $body +
        $text.Substring($bodyIndex + $section.Groups['body'].Length)
    return ,$latin1.GetBytes($updated)
}

function Set-GameDisplayMode {
    param([byte[]]$Bytes)

    $decoded = [byte[]]$Bytes.Clone()
    for ($index = 0; $index -lt $decoded.Length; $index++) {
        $decoded[$index] = $decoded[$index] -bxor 0x78
    }
    $keyBytes = $gameEncoding.GetBytes('显示模式=')
    $keyText = $latin1.GetString($keyBytes)
    $pattern = '(?m)^' + [regex]::Escape($keyText) + '(?<mode>[012])(?=\r?$)'
    $modeMatches = [regex]::Matches($latin1.GetString($decoded), $pattern)
    if ($modeMatches.Count -ne 1) {
        throw 'Ini\Config.ini has an unsupported display-mode entry.'
    }

    # Change one encrypted byte. Preserve the original header and all other data.
    $updated = [byte[]]$Bytes.Clone()
    $updated[$modeMatches[0].Groups['mode'].Index] = [byte][char]'2' -bxor 0x78
    return ,$updated
}

function Read-BackupState {
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.Version -ne 1 -or $state.GameDirectory -ine $GameDirectory) {
        throw 'The backup metadata belongs to a different game directory or format.'
    }
    $names = @($state.Files | ForEach-Object { $_.RelativePath })
    $unknownNames = @($names | Where-Object { $affectedNames -notcontains $_ })
    $missingOriginalNames = @($originalAffectedNames | Where-Object { $names -notcontains $_ })
    if ($unknownNames.Count -gt 0 -or $missingOriginalNames.Count -gt 0 -or
        @($names | Select-Object -Unique).Count -ne $names.Count) {
        throw 'The backup metadata does not contain the expected file list.'
    }
    foreach ($entry in $state.Files) {
        if ($entry.OriginalExists -isnot [bool] -or
            $entry.InstalledSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
            ($entry.OriginalExists -and $entry.OriginalSha256 -notmatch '^[A-Fa-f0-9]{64}$')) {
            throw 'The backup metadata contains invalid file information.'
        }
        if ($entry.OriginalExists) {
            $originalPath = Join-Path $backupDirectory ('files\' + $entry.RelativePath)
            if ((Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -ine $entry.OriginalSha256) {
                throw "The original backup failed its hash check. $($entry.RelativePath)"
            }
        }
    }
    return $state
}

function Convert-StateToBytes {
    param($State)

    $json = $State | ConvertTo-Json -Depth 5
    return ,[Text.Encoding]::UTF8.GetBytes($json + [Environment]::NewLine)
}

function Invoke-GameFileChanges {
    param([Collections.IDictionary]$Changes)

    $previous = [ordered]@{}
    foreach ($name in $Changes.Keys) {
        $path = Join-Path $GameDirectory $name
        $previous[$name] = $null
        if ([IO.File]::Exists($path)) {
            $previous[$name] = [IO.File]::ReadAllBytes($path)
        }
    }
    try {
        foreach ($name in $Changes.Keys) {
            $path = Join-Path $GameDirectory $name
            if ($null -eq $Changes[$name]) {
                if ([IO.File]::Exists($path)) {
                    Remove-Item -LiteralPath $path -Force
                }
            }
            else {
                Write-BytesAtomically -Path $path -Bytes $Changes[$name]
            }
        }
    }
    catch {
        $failure = $_
        foreach ($name in $previous.Keys) {
            try {
                $path = Join-Path $GameDirectory $name
                if ($null -eq $previous[$name]) {
                    if ([IO.File]::Exists($path)) {
                        Remove-Item -LiteralPath $path -Force
                    }
                }
                else {
                    Write-BytesAtomically -Path $path -Bytes $previous[$name]
                }
            }
            catch {
                Write-Warning "Could not restore $name after the failed operation. $($_.Exception.Message)"
            }
        }
        throw $failure
    }
}

if (-not (Test-Path -LiteralPath $GameDirectory -PathType Container)) {
    throw "Game directory does not exist. $GameDirectory"
}
$GameDirectory = (Get-Item -LiteralPath $GameDirectory).FullName.TrimEnd('\')
$backupDirectory = Join-Path $GameDirectory 'SealHD-backup'
$statePath = Join-Path $backupDirectory 'backup.json'
$targetExecutable = Join-Path $GameDirectory 'SOE.EXE'
$runningTarget = Get-Process -Name SOE -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -ieq $targetExecutable }
if ($runningTarget) {
    throw 'Close Seal of Evil before changing its files.'
}
if ([long]$Width * $Height -gt 33177600) {
    throw 'The requested resolution exceeds the patch limit of 33177600 pixels.'
}
$profileIndexes = @{
    '800x600' = 0
    '1024x768' = 1
    '1920x1080' = 2
    '2560x1440' = 3
    '3840x2160' = 4
}
$requestedResolution = "${Width}x${Height}"
if ($Action -ne 'Uninstall' -and -not $profileIndexes.ContainsKey($requestedResolution)) {
    throw 'Choose 800x600, 1024x768, 1920x1080, 2560x1440, or 3840x2160.'
}
$profileIndex = $profileIndexes[$requestedResolution]

$changes = [ordered]@{}
if ($Action -eq 'Uninstall') {
    if (-not [IO.File]::Exists($statePath)) {
        throw 'The SealHD backup metadata is missing.'
    }
    $state = Read-BackupState
    $preserved = @()
    foreach ($entry in $state.Files) {
        if ($entry.OriginalExists) {
            $backupPath = Join-Path $backupDirectory ('files\' + $entry.RelativePath)
            $bytes = [IO.File]::ReadAllBytes($backupPath)
            if ((Get-BytesHash $bytes) -ine $entry.OriginalSha256) {
                throw "The original backup failed its hash check. $($entry.RelativePath)"
            }
            $changes[$entry.RelativePath] = $bytes
        }
        else {
            $path = Join-Path $GameDirectory $entry.RelativePath
            if ([IO.File]::Exists($path)) {
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ieq $entry.InstalledSha256) {
                    $changes[$entry.RelativePath] = $null
                }
                else {
                    $preserved += $entry.RelativePath
                }
            }
        }
    }
    Invoke-GameFileChanges $changes
    Write-Output 'Restored the original game files. The backup has been retained.'
    foreach ($name in $preserved) {
        Write-Output "Preserved a changed file that was created after the original installation. $name"
    }
    return
}

$requiredGameHashes = [ordered]@{
    'SOE.EXE' = '452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B'
    'Gfx3d.dll' = '1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078'
}
foreach ($name in $requiredGameHashes.Keys) {
    $path = Join-Path $GameDirectory $name
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $requiredGameHashes[$name]) {
        throw "This game build is unsupported. $name failed its SHA256 check."
    }
}

if ($Action -eq 'Install') {
    $payloadDirectory = Join-Path $PSScriptRoot 'payload'
    foreach ($name in $payloadNames) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $payloadDirectory $name))
        if ($bytes.Length -eq 0) {
            throw "The package payload is empty. $name"
        }
        if ($name.EndsWith('.dll')) {
            if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
                throw "The package contains an invalid DLL. $name"
            }
            $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
            if ($peOffset -lt 0 -or $peOffset -gt $bytes.Length - 6 -or
                [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550 -or
                [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x14C) {
                throw "The package requires an x86 DLL. $name"
            }
        }
        if ($name.EndsWith('.png')) {
            if ($bytes.Length -lt 24 -or
                [BitConverter]::ToString($bytes, 0, 8) -cne '89-50-4E-47-0D-0A-1A-0A') {
                throw "The package contains an invalid PNG. $name"
            }
            $imageWidth = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 16))
            $imageHeight = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 20))
            if ($imageWidth -ne 3840 -or $imageHeight -ne 2160) {
                throw "The package requires a 3840x2160 image. $name"
            }
        }
        $changes[$name] = $bytes
    }
    $changes['SealHD.ini'] = Set-HDDisplaySettings $changes['SealHD.ini']
    $hdText = $latin1.GetString($changes['SealHD.ini'])
    $compatibilitySections = [regex]::Matches(
        $hdText, '(?ims)^\[Compatibility\][ \t]*\r?\n(?<body>.*?)(?=^\[|\z)'
    )
    if ($compatibilitySections.Count -ne 1) {
        throw 'The package SealHD.ini requires exactly one [Compatibility] section.'
    }
    $requiredCompatibilitySettings = [ordered]@{
        ForceWindowed = 1
        VirtualFullscreen = 1
    }
    foreach ($key in $requiredCompatibilitySettings.Keys) {
        $entries = [regex]::Matches(
            $compatibilitySections[0].Groups['body'].Value,
            '(?im)^' + $key + '[ \t]*=[ \t]*(?<value>[^\r\n]*)\r?$'
        )
        $requiredValue = [string]$requiredCompatibilitySettings[$key]
        if ($entries.Count -ne 1 -or $entries[0].Groups['value'].Value.Trim() -cne $requiredValue) {
            throw "The package SealHD.ini requires $key=$requiredValue."
        }
    }
    $wrapperText = $latin1.GetString($changes['dgVoodoo.conf'])
    if ($wrapperText -notmatch '(?im)^Version[ \t]*=[ \t]*0x[0-9a-f]+[ \t]*\r?$' -or
        $wrapperText -notmatch '(?im)^\[General\][ \t]*\r?$' -or
        $wrapperText -notmatch '(?im)^\[DirectX\][ \t]*\r?$') {
        throw 'The package dgVoodoo.conf is invalid.'
    }
}
else {
    if (-not [IO.File]::Exists($statePath)) {
        throw 'Install SealHD before changing the resolution.'
    }
    $changes['SealHD.ini'] = Set-HDDisplaySettings ([IO.File]::ReadAllBytes((Join-Path $GameDirectory 'SealHD.ini')))
}
$changes['Ini\Config.ini'] = Set-GameDisplayMode ([IO.File]::ReadAllBytes((Join-Path $GameDirectory 'Ini\Config.ini')))

if ([IO.File]::Exists($statePath)) {
    $state = Read-BackupState
}
else {
    $state = [pscustomobject]@{
        Version = 1
        GameDirectory = $GameDirectory
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        Files = @()
    }
}

if ($Action -eq 'Install') {
    # Older backups have seven entries. Append newly introduced assets while
    # preserving every existing original file and its recorded hash.
    $addedEntries = $false
    foreach ($name in $affectedNames) {
        if (@($state.Files | Where-Object { $_.RelativePath -ieq $name }).Count -gt 0) {
            continue
        }
        $path = Join-Path $GameDirectory $name
        $exists = [IO.File]::Exists($path)
        $originalHash = $null
        if ($exists) {
            $bytes = [IO.File]::ReadAllBytes($path)
            $backupPath = Join-Path $backupDirectory ('files\' + $name)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backupPath)) | Out-Null
            Write-BytesAtomically -Path $backupPath -Bytes $bytes
            $originalHash = Get-BytesHash $bytes
        }
        $state.Files = @($state.Files) + [pscustomobject]@{
            RelativePath = $name
            OriginalExists = $exists
            OriginalSha256 = $originalHash
            InstalledSha256 = Get-BytesHash $changes[$name]
        }
        $addedEntries = $true
    }
    if ($addedEntries) {
        # Persist the original-existence records before installing new assets.
        # A retry can then reuse the first backups after an interrupted install.
        Write-BytesAtomically -Path $statePath -Bytes (Convert-StateToBytes $state)
    }
}

foreach ($entry in $state.Files) {
    if ($changes.Contains($entry.RelativePath)) {
        $entry.InstalledSha256 = Get-BytesHash $changes[$entry.RelativePath]
    }
}
$changes['SealHD-backup\backup.json'] = Convert-StateToBytes $state
Invoke-GameFileChanges $changes
Write-Output "SealHD $Action completed at ${Width}x${Height}. ProfileIndex=$profileIndex Borderless=$([int]$Borderless)"
Write-Output "Game directory $GameDirectory"
