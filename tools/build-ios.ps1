# Exports the Unity-generated Xcode project for iOS (Roadmap 10.2).
#
#   .\tools\build-ios.ps1
#   .\tools\build-ios.ps1 -UnityPath "C:\Path\To\Unity.exe"
#
# IMPORTANT: this produces an XCODE PROJECT, not an .ipa. Apple only allows an iOS app
# to be built, signed and uploaded from macOS, so the App Store step is:
#
#   1. run this (on Windows or Mac) -> client/builds/ios
#   2. on a Mac: open Unity-iPhone.xcodeproj in Xcode
#   3. set the Team / signing certificate + provisioning profile
#   4. Product > Archive, then Distribute App > App Store Connect
#
# Running it on Windows is still useful: it validates that the iOS player settings and
# the IL2CPP scripting backend export cleanly before you get near a Mac.
#
# Requires "iOS Build Support" installed in Unity Hub for this editor version.
# ASCII-only and PowerShell 5.1 safe.
param(
    [string]$UnityPath = "",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root "client"

if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path $projectPath "builds/ios"
}

# Resolve the editor version from the project so we use the matching Unity.
$version = (Get-Content (Join-Path $projectPath "ProjectSettings/ProjectVersion.txt") |
    Select-String "m_EditorVersion:").ToString().Split(":")[1].Trim()

if ([string]::IsNullOrEmpty($UnityPath)) {
    # Editor install roots: the two Hub defaults plus any custom location Unity Hub
    # records in secondaryInstallPath.json (e.g. another drive).
    $roots = @(
        "C:\Program Files\Unity\Hub\Editor",
        "$env:LOCALAPPDATA\Programs\Unity\Hub\Editor"
    )
    $secondary = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path $secondary) {
        try { $custom = (Get-Content $secondary -Raw | ConvertFrom-Json) } catch { $custom = "" }
        if (-not [string]::IsNullOrWhiteSpace($custom)) { $roots += $custom }
    }

    $UnityPath = $roots |
        ForEach-Object { Join-Path $_ "$version\Editor\Unity.exe" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1

    if ([string]::IsNullOrEmpty($UnityPath)) {
        $UnityPath = $roots | Where-Object { Test-Path $_ } | ForEach-Object {
            Get-ChildItem -Path $_ -Recurse -Filter Unity.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*$version*Editor\Unity.exe" }
        } | Select-Object -First 1 -ExpandProperty FullName
    }
}

if ([string]::IsNullOrEmpty($UnityPath) -or -not (Test-Path $UnityPath)) {
    Write-Error "Unity $version not found. Pass -UnityPath 'C:\...\Unity.exe' explicitly."
}

$logFile = Join-Path $projectPath "builds/ios-build.log"
New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null

$unityArgs = @(
    "-quit", "-batchmode", "-nographics",
    "-projectPath", $projectPath,
    "-buildTarget", "iOS",
    "-executeMethod", "Fts.EditorTools.MobileBuilder.BuildiOS",
    "-buildOutput", $Output,
    "-logFile", $logFile
)

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $projectPath"
Write-Host "Output:  $Output"
Write-Host "Exporting the iOS Xcode project (this can take several minutes)..." -ForegroundColor Cyan
Write-Warning "Close any open Unity Editor on this project first - a second instance will crash the batch build."

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

# Unity.exe is a GUI-subsystem app; Start-Process -Wait -PassThru captures the exit code
# reliably where the call operator does not, and the produced folder is the real check.
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$code = $proc.ExitCode

if (Test-Path (Join-Path $Output "Unity-iPhone.xcodeproj")) {
    Write-Host "iOS export succeeded -> $Output (exit $code)" -ForegroundColor Green
    Write-Host "Continue on a Mac: open Unity-iPhone.xcodeproj, set signing, Product > Archive." -ForegroundColor Green
} else {
    Write-Error "iOS export FAILED (exit $code). See log: $logFile"
}
