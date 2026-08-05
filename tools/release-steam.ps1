# Packages and uploads the desktop builds to Steam (Roadmap 10.2).
#
#   .\tools\release-steam.ps1 -DryRun            # resolve the VDFs + report, upload nothing
#   .\tools\release-steam.ps1 -Build             # build Windows+Mac+Linux first, then upload
#   .\tools\release-steam.ps1 -SteamLogin myuser # upload with an existing local build
#   .\tools\release-steam.ps1 -Preview           # steamcmd validates and reports, uploads nothing
#
# What it does:
#   1. reads tools\version.json for the build description (so a Steam build is traceable
#      to the same version number the stores and /health report);
#   2. resolves tools\steam\*.template.vdf against tools\steam\steam.config.json into
#      client\builds\steam\ (gitignored);
#   3. optionally runs tools\build-desktop.ps1 -Platform All;
#   4. runs steamcmd +run_app_build with the resolved script.
#
# NOT included on purpose: the Steamworks SDK (no App ID yet, per the 6.5 decision to stay
# "Steam-ready"). Overlay / achievements / cloud saves are a later step.
#
# Credentials: this script NEVER stores a password. Log steamcmd in once by hand
# (`steamcmd +login <user>` and answer the Steam Guard prompt); the cached session is
# what subsequent runs use.
#
# ASCII-only and PowerShell 5.1 safe.
param(
    [string]$SteamCmd = "",
    [string]$SteamLogin = "",
    [switch]$Build,
    [switch]$DryRun,
    [switch]$Preview
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$steamDir = Join-Path $PSScriptRoot "steam"
$buildsDir = Join-Path $root "client/builds"
$outDir = Join-Path $buildsDir "steam"

# ---------------------------------------------------------------- config
$cfgPath = Join-Path $steamDir "steam.config.json"
if (-not (Test-Path $cfgPath)) { Write-Error "Missing $cfgPath" }
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json

$version = (Get-Content (Join-Path $PSScriptRoot "version.json") -Raw | ConvertFrom-Json)

# Refuse to pretend: placeholder ids mean the Steamworks app does not exist yet.
$placeholder = ($cfg.appId -eq "0000000")
if ($placeholder -and -not $DryRun) {
    Write-Error "steam.config.json still holds placeholder ids. Fill in the App ID and depot ids from the Steamworks partner site, or run with -DryRun."
}

# ---------------------------------------------------------------- build (optional)
if ($Build) {
    Write-Host "Building all desktop targets first..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build-desktop.ps1") -Platform All
}

# A depot with no content root is an upload of nothing - catch it before steamcmd does.
$missing = @()
foreach ($p in @("windows", "mac", "linux")) {
    $dir = Join-Path $buildsDir $p
    if (-not (Test-Path $dir) -or ((Get-ChildItem $dir -ErrorAction SilentlyContinue).Count -eq 0)) {
        $missing += $p
    }
}
if ($missing.Count -gt 0) {
    $msg = "No build content for: {0}. Run .\tools\build-desktop.ps1 -Platform All (or pass -Build)." -f ($missing -join ", ")
    if ($DryRun) { Write-Warning $msg } else { Write-Error $msg }
}

# ---------------------------------------------------------------- resolve the VDFs
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# steamcmd wants absolute native paths in the VDFs.
$contentRoot = (Resolve-Path $buildsDir).Path
$buildOutput = $outDir
$desc = "FTS {0} ({1}) {2}" -f $version.version, $version.channel, (Get-Date -Format "yyyy-MM-dd HH:mm")

$map = @{
    "{{APP_ID}}"        = $cfg.appId
    "{{DEPOT_WINDOWS}}" = $cfg.depots.windows
    "{{DEPOT_MAC}}"     = $cfg.depots.mac
    "{{DEPOT_LINUX}}"   = $cfg.depots.linux
    "{{CONTENT_ROOT}}"  = $contentRoot
    "{{BUILD_OUTPUT}}"  = $buildOutput
    "{{BUILD_DESC}}"    = $desc
    "{{BRANCH}}"        = $cfg.branch
    "{{PREVIEW}}"       = $(if ($Preview) { "1" } else { "0" })
}

foreach ($tpl in Get-ChildItem $steamDir -Filter "*.template.vdf") {
    $text = Get-Content $tpl.FullName -Raw
    foreach ($k in $map.Keys) { $text = $text.Replace($k, [string]$map[$k]) }
    $target = Join-Path $outDir ($tpl.Name -replace "\.template\.vdf$", ".vdf")
    [System.IO.File]::WriteAllText($target, $text)
    Write-Host ("Resolved -> {0}" -f $target)
}

$appScript = Join-Path $outDir "app_build.vdf"

Write-Host ""
Write-Host ("App ID       : {0}" -f $cfg.appId)
Write-Host ("Description  : {0}" -f $desc)
Write-Host ("Content root : {0}" -f $contentRoot)
Write-Host ("Set live on  : {0}" -f $(if ([string]::IsNullOrWhiteSpace($cfg.branch)) { "(nothing - upload only)" } else { $cfg.branch }))

if ($DryRun) {
    Write-Host ""
    Write-Host "Dry run: VDFs written, steamcmd NOT invoked." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------- steamcmd
if ([string]::IsNullOrWhiteSpace($SteamCmd)) {
    $candidates = @(
        "C:\steamcmd\steamcmd.exe",
        "C:\Program Files (x86)\Steam\steamcmd.exe",
        "$env:LOCALAPPDATA\steamcmd\steamcmd.exe"
    )
    $SteamCmd = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($SteamCmd)) {
        $cmd = Get-Command steamcmd.exe -ErrorAction SilentlyContinue
        if ($cmd) { $SteamCmd = $cmd.Source }
    }
}
if ([string]::IsNullOrWhiteSpace($SteamCmd) -or -not (Test-Path $SteamCmd)) {
    Write-Error "steamcmd.exe not found. Install the Steamworks SDK content builder and pass -SteamCmd 'C:\...\steamcmd.exe'."
}
if ([string]::IsNullOrWhiteSpace($SteamLogin)) {
    Write-Error "Pass -SteamLogin <steamworks account>. Log that account in once by hand first so Steam Guard is cached."
}

Write-Host ""
Write-Host "Uploading via steamcmd (this can take a while)..." -ForegroundColor Cyan
# NOT $args - that is an automatic variable in PowerShell.
$cmdArgs = @("+login", $SteamLogin, "+run_app_build", $appScript, "+quit")
$proc = Start-Process -FilePath $SteamCmd -ArgumentList $cmdArgs -Wait -PassThru -NoNewWindow

if ($proc.ExitCode -eq 0) {
    Write-Host "Steam upload finished (exit 0). Check the Builds page on the partner site." -ForegroundColor Green
} else {
    Write-Error ("steamcmd FAILED (exit {0}). Logs: {1}" -f $proc.ExitCode, (Join-Path $outDir "*.log"))
}
