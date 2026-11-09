param([switch]$Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'resolutions.json') -Raw | ConvertFrom-Json
$profiles = @($catalog.Profiles | Sort-Object Id)
if ($profiles.Count -eq 0) { throw 'No resolution profiles.' }
$seen = @{}
for ($index = 0; $index -lt $profiles.Count; ++$index) {
    $profile = $profiles[$index]
    if ($profile.Id -ne $index -or $profile.Width -lt 640 -or $profile.Height -lt 480 -or
        $profile.Width -gt 7680 -or $profile.Height -gt 4320 -or
        ([long]$profile.Width * $profile.Height) -gt 33177600 -or $profile.Aspect -notin @('4:3','16:9','16:10')) {
        throw 'Invalid or non-contiguous stable resolution ID.'
    }
    $dimensions = '{0}x{1}' -f $profile.Width, $profile.Height
    if ($seen.ContainsKey($dimensions)) { throw 'Duplicate resolution.' }
    $seen[$dimensions] = $true
}
$order = @($catalog.SelectionOrder)
if ($order.Count -ne $profiles.Count -or @($order | Sort-Object -Unique).Count -ne $profiles.Count -or
    @($order | Where-Object { $_ -lt 0 -or $_ -ge $profiles.Count }).Count -gt 0) { throw 'Invalid selection order.' }
$legacy = @('800x600','1024x768','1920x1080','2560x1440','3840x2160')
for ($index = 0; $index -lt $legacy.Count; ++$index) {
    if (('{0}x{1}' -f $profiles[$index].Width,$profiles[$index].Height) -ne $legacy[$index]) { throw 'A legacy profile ID was reassigned.' }
}
if ($catalog.DefaultId -ne 2) { throw 'The default must retain the legacy 1080p ID.' }
$cpp = @('// Generated from config/resolutions.json. Do not edit by hand.','#pragma once','','namespace seal_resolution {',
    'struct ResolutionProfile { unsigned width, height; const char *label; const char *aspect; };',
    'static const ResolutionProfile kResolutionProfiles[] = {')
$cs = @('// Generated from config/resolutions.json. Do not edit by hand.','namespace SealHdInstaller','{',
    '    internal sealed class ResolutionProfile','    {',
    '        internal readonly int Id, Width, Height;','        internal readonly string Aspect;',
    '        internal ResolutionProfile(int id, int width, int height, string aspect)',
    '        { Id = id; Width = width; Height = height; Aspect = aspect; }',
    '        public override string ToString() { return Width + " x " + Height + " (" + Aspect + ")"; }','    }','',
    '    internal static class ResolutionProfiles','    {',
    '        internal static readonly ResolutionProfile[] All = {')
foreach ($profile in $profiles) {
    $cpp += '    {{ {0}, {1}, "{0}*{1}", "{2}" }}, // Stable ID {3}' -f $profile.Width,$profile.Height,$profile.Aspect,$profile.Id
    $cs += '            new ResolutionProfile({0}, {1}, {2}, "{3}"),' -f $profile.Id,$profile.Width,$profile.Height,$profile.Aspect
}
$cpp += @('};',('static const int kResolutionOrder[] = { ' + ($order -join ', ') + ' };'),
    'static const unsigned kResolutionCount = sizeof(kResolutionProfiles) / sizeof(kResolutionProfiles[0]);',
    'static const int kDefaultResolutionId = 2;','} // namespace seal_resolution','')
$cs += @('        };','        internal static readonly ResolutionProfile[] Ordered = {')
foreach ($id in $order) { $cs += '            All[{0}],' -f $id }
$cs += @('        };','        internal static ResolutionProfile Default { get { return All[2]; } }',
    '        internal static ResolutionProfile ById(int id) { return id >= 0 && id < All.Length ? All[id] : null; }',
    '        internal static ResolutionProfile Find(int width, int height)',
    '        { foreach (var profile in All) if (profile.Width == width && profile.Height == height) return profile; return null; }',
    '    }','}','')
$outputs = @(@{Path=(Join-Path $root 'source/resolution_profiles.h');Lines=$cpp},@{Path=(Join-Path $root 'installer/src/ResolutionProfiles.cs');Lines=$cs})
foreach ($output in $outputs) {
    $content = ($output.Lines -join "`n")
    if ($Check) {
        if (-not [IO.File]::Exists($output.Path) -or [IO.File]::ReadAllText($output.Path).Replace("`r`n","`n") -cne $content) { throw ('Generated resolution catalog is stale: ' + $output.Path) }
    } else { [IO.File]::WriteAllText($output.Path,$content,(New-Object Text.UTF8Encoding($false))) }
}
$extra = @($order | Where-Object { $_ -ge 2 } | ForEach-Object { '{0}x{1}' -f $profiles[$_].Width,$profiles[$_].Height })
if ($extra.Count -gt 16) { throw 'dgVoodoo supports at most 16 extra enumerated resolutions.' }
$config = Join-Path $root 'config/dgVoodoo.conf'
$existing = [IO.File]::ReadAllText($config)
$expectedLine = 'ExtraEnumeratedResolutions          = ' + ($extra -join ', ')
$pattern = '(?m)^ExtraEnumeratedResolutions[^\r\n]*'
if ([regex]::Matches($existing,$pattern).Count -ne 1) { throw 'Expected exactly one wrapper resolution list.' }
$updated = [regex]::Replace($existing,$pattern,[Text.RegularExpressions.MatchEvaluator]{ param($match) $expectedLine })
if ($Check) {
    if ($existing -cne $updated) { throw 'The wrapper resolution list is stale.' }
} else { [IO.File]::WriteAllText($config,$updated,(New-Object Text.UTF8Encoding($false))) }
Write-Output ('Resolution catalog verified: {0} stable IDs, legacy IDs preserved.' -f $profiles.Count)
