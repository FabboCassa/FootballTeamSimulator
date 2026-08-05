# Builds a SIGNED Google Play App Bundle (Roadmap 10.2).
#
#   .\tools\release-android.ps1                 # signed .aab at the current version
#   .\tools\release-android.ps1 -BumpBuild      # +1 versionCode first (Play refuses a reused one)
#   .\tools\release-android.ps1 -Apk            # signed .apk instead (sideload / device testing)
#
# Credentials come from a LOCAL, GITIGNORED file: tools\keystore.local.ps1
#
#     $env:FTS_ANDROID_KEYSTORE      = "C:\keys\fts-release.keystore"
#     $env:FTS_ANDROID_KEYSTORE_PASS = "..."
#     $env:FTS_ANDROID_KEYALIAS      = "fts"
#     $env:FTS_ANDROID_KEYALIAS_PASS = "..."
#
# They are passed to Unity as environment variables rather than command-line arguments,
# because Unity writes its command line into the editor log and a password given as an
# argument would be sitting in client\builds\android-build.log.
#
# Create the keystore once (JDK keytool, e.g. the one shipped with Unity's Android module):
#
#     keytool -genkeypair -v -keystore fts-release.keystore -alias fts \
#             -keyalg RSA -keysize 2048 -validity 10000
#
# BACK THAT FILE UP OFF THE MACHINE. Without it you can never update the app on Play
# under the same identity - unless Play App Signing is enabled, which is why the
# checklist recommends enabling it at the first upload.
#
# ASCII-only and PowerShell 5.1 safe.
param(
    [switch]$Apk,
    [switch]$BumpBuild,
    [string]$UnityPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$localCreds = Join-Path $PSScriptRoot "keystore.local.ps1"

if (Test-Path $localCreds) {
    Write-Host "Loading signing credentials from tools\keystore.local.ps1"
    . $localCreds
}

$required = @("FTS_ANDROID_KEYSTORE", "FTS_ANDROID_KEYSTORE_PASS", "FTS_ANDROID_KEYALIAS", "FTS_ANDROID_KEYALIAS_PASS")
$missing = $required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
if ($missing.Count -gt 0) {
    Write-Error ("Missing signing credentials: {0}. Create tools\keystore.local.ps1 (see the header of this script)." -f ($missing -join ", "))
}
if (-not (Test-Path $env:FTS_ANDROID_KEYSTORE)) {
    Write-Error ("Keystore not found at {0}" -f $env:FTS_ANDROID_KEYSTORE)
}

# One version, applied everywhere, before the build reads it.
$setVersion = Join-Path $PSScriptRoot "set-version.ps1"
if ($BumpBuild) { & $setVersion -BumpBuild } else { & $setVersion }

$version = (Get-Content (Join-Path $PSScriptRoot "version.json") -Raw | ConvertFrom-Json)
$name = if ($Apk) { "FootballTeamSimulator-{0}.apk" } else { "FootballTeamSimulator-{0}.aab" }
$output = Join-Path $root ("client/builds/android/" + ($name -f $version.version))

Write-Host ""
Write-Host ("Version     : {0} (versionCode {1})" -f $version.version, $version.androidVersionCode)
Write-Host ("Signing key : {0} (alias {1})" -f $env:FTS_ANDROID_KEYSTORE, $env:FTS_ANDROID_KEYALIAS)
Write-Host ("Output      : {0}" -f $output)
Write-Host ""

$buildArgs = @{ Output = $output }
if ($UnityPath -ne "") { $buildArgs["UnityPath"] = $UnityPath }
if (-not $Apk) { $buildArgs["Aab"] = $true }

& (Join-Path $PSScriptRoot "build-android.ps1") @buildArgs

if (Test-Path $output) {
    Write-Host ""
    Write-Host "Signed build ready. Next steps:" -ForegroundColor Green
    if ($Apk) {
        Write-Host ("  adb install -r `"{0}`"" -f $output)
    } else {
        Write-Host "  1. Play Console > Testing > Internal testing > Create new release"
        Write-Host ("  2. Upload {0}" -f $output)
        Write-Host "  3. Fill the release notes, then roll out to the internal testers"
        Write-Host "  See docs/store/release-checklist.md for the full submission list."
    }
}
