# Applies ONE version across every shipping target (Roadmap 10.2).
#
#   .\tools\set-version.ps1                       # apply tools\version.json as-is
#   .\tools\set-version.ps1 -Version 0.2.0        # set a new marketing version
#   .\tools\set-version.ps1 -BumpBuild            # +1 to androidVersionCode and iosBuildNumber
#   .\tools\set-version.ps1 -Check                # report only, change nothing (CI-friendly)
#
# tools\version.json is the single source of truth. This script writes it into:
#   * client/ProjectSettings/ProjectSettings.asset  -> bundleVersion (all platforms),
#     AndroidBundleVersionCode, buildNumber.Standalone, buildNumber.iPhone
#   * server/Api/Api.csproj                         -> <Version> (surfaced by GET /health)
#
# Store rules this exists to satisfy: Google Play REFUSES an upload whose versionCode
# was already used, App Store Connect the same for CFBundleVersion. So the build number
# only ever goes up, and it goes up in one place.
#
# ASCII-only and PowerShell 5.1 safe, like every other script in tools/.
param(
    [string]$Version = "",
    [switch]$BumpBuild,
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $PSScriptRoot "version.json"
$projectSettings = Join-Path $root "client/ProjectSettings/ProjectSettings.asset"
$apiCsproj = Join-Path $root "server/Api/Api.csproj"

if (-not (Test-Path $versionFile)) { Write-Error "Missing $versionFile" }
$cfg = Get-Content $versionFile -Raw | ConvertFrom-Json

if (-not [string]::IsNullOrWhiteSpace($Version)) { $cfg.version = $Version }
if ($BumpBuild) {
    $cfg.androidVersionCode = [int]$cfg.androidVersionCode + 1
    $cfg.iosBuildNumber = [int]$cfg.iosBuildNumber + 1
}

# A marketing version must be x.y.z - both stores parse it and Steam's build description
# reads better for it. Fail loudly rather than shipping "1.0" to one store and "1.0.0" to another.
if ($cfg.version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error ("Version '{0}' is not x.y.z (e.g. 0.2.0)." -f $cfg.version)
}

Write-Host ("Version      : {0} ({1})" -f $cfg.version, $cfg.channel)
Write-Host ("Android code : {0}" -f $cfg.androidVersionCode)
Write-Host ("iOS build    : {0}" -f $cfg.iosBuildNumber)

if ($Check) {
    # Report what the files currently hold so CI (or a human) can spot a drift.
    $ps = Get-Content $projectSettings -Raw
    $bundle = ([regex]::Match($ps, '(?m)^\s*bundleVersion:\s*(.+)$')).Groups[1].Value.Trim()
    $code = ([regex]::Match($ps, '(?m)^\s*AndroidBundleVersionCode:\s*(\d+)')).Groups[1].Value
    $csproj = Get-Content $apiCsproj -Raw
    $apiVer = ([regex]::Match($csproj, '<Version>(.*?)</Version>')).Groups[1].Value
    Write-Host ""
    Write-Host ("ProjectSettings bundleVersion : {0}" -f $bundle)
    Write-Host ("ProjectSettings versionCode   : {0}" -f $code)
    Write-Host ("Api.csproj Version            : {0}" -f $(if ($apiVer) { $apiVer } else { "(absent)" }))
    $drift = ($bundle -ne $cfg.version) -or ($code -ne [string]$cfg.androidVersionCode) -or ($apiVer -ne $cfg.version)
    if ($drift) { Write-Warning "Files DO NOT match version.json - run without -Check to apply." }
    else { Write-Host "All targets match version.json." -ForegroundColor Green }
    exit $(if ($drift) { 1 } else { 0 })
}

# ---------------------------------------------------------------- ProjectSettings
# Line-by-line with a small state machine: 'Standalone:' and 'iPhone:' also appear under
# scriptingBackend / applicationIdentifier, so the buildNumber ones must be scoped to
# their own block. Newline style is preserved by splitting and rejoining on what the
# file actually uses (Unity writes LF even on Windows).
$raw = Get-Content $projectSettings -Raw
$newline = if ($raw -match "`r`n") { "`r`n" } else { "`n" }
$lines = $raw -split "`r?`n"
$inBuildNumber = $false

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]

    if ($line -match '^\s*buildNumber:\s*$') { $inBuildNumber = $true; continue }
    # Any key at the same indentation as buildNumber ends its block.
    if ($inBuildNumber -and $line -notmatch '^\s{4,}') { $inBuildNumber = $false }

    if ($inBuildNumber) {
        if ($line -match '^(\s*)(Standalone|iPhone):\s*\d+\s*$') {
            $lines[$i] = "{0}{1}: {2}" -f $Matches[1], $Matches[2], $cfg.iosBuildNumber
        }
        continue
    }

    if ($line -match '^(\s*)bundleVersion:\s*.+$') {
        $lines[$i] = "{0}bundleVersion: {1}" -f $Matches[1], $cfg.version
    }
    elseif ($line -match '^(\s*)(tvOSBundleVersion|visionOSBundleVersion):\s*.+$') {
        $lines[$i] = "{0}{1}: {2}" -f $Matches[1], $Matches[2], $cfg.version
    }
    elseif ($line -match '^(\s*)AndroidBundleVersionCode:\s*\d+\s*$') {
        $lines[$i] = "{0}AndroidBundleVersionCode: {1}" -f $Matches[1], $cfg.androidVersionCode
    }
}

# WriteAllText keeps UTF-8 WITHOUT a BOM (Set-Content -Encoding UTF8 on PS 5.1 would add one,
# and a BOM in a Unity YAML asset is a needless diff).
[System.IO.File]::WriteAllText($projectSettings, ($lines -join $newline))
Write-Host "Updated client/ProjectSettings/ProjectSettings.asset" -ForegroundColor Green

# ---------------------------------------------------------------- Api.csproj
$csprojRaw = Get-Content $apiCsproj -Raw
if ($csprojRaw -match '<Version>.*?</Version>') {
    $csprojRaw = [regex]::Replace($csprojRaw, '<Version>.*?</Version>', ("<Version>{0}</Version>" -f $cfg.version))
} else {
    # Insert into the first PropertyGroup, matching its indentation.
    $csprojRaw = [regex]::Replace(
        $csprojRaw,
        '(?s)(<PropertyGroup>\s*\r?\n)',
        ('${1}    <Version>' + $cfg.version + '</Version>' + [Environment]::NewLine),
        1)
}
[System.IO.File]::WriteAllText($apiCsproj, $csprojRaw)
Write-Host "Updated server/Api/Api.csproj" -ForegroundColor Green

# ---------------------------------------------------------------- version.json
($cfg | ConvertTo-Json -Depth 4) | Set-Content -Path $versionFile -Encoding ASCII
Write-Host "Updated tools/version.json" -ForegroundColor Green
Write-Host ""
Write-Host "Remember to commit ProjectSettings.asset, Api.csproj and version.json together." -ForegroundColor Yellow
