# Builds the Unity Android player headlessly (Roadmap 6.4).
#   .\tools\build-android.ps1                 # APK, auto-detect Unity 6000.3.17f1
#   .\tools\build-android.ps1 -Aab            # Google Play App Bundle (.aab)
#   .\tools\build-android.ps1 -UnityPath "C:\Path\To\Unity.exe"
#   .\tools\build-android.ps1 -Output "C:\out\android\game.apk"
#
# Requires:
#   * the Boot scene enabled in File > Build Settings;
#   * "Android Build Support" (with OpenJDK + SDK/NDK) installed in Unity Hub
#     for this editor version.
#
# Output goes to client/builds/android (gitignored). Install an APK on a device
# with:  adb install -r client\builds\android\FootballTeamSimulator.apk
param(
    [string]$UnityPath = "",
    [string]$Output = "",
    [switch]$Aab
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root "client"

if ([string]::IsNullOrEmpty($Output)) {
    $fileName = if ($Aab) { "FootballTeamSimulator.aab" } else { "FootballTeamSimulator.apk" }
    $Output = Join-Path $projectPath (Join-Path "builds/android" $fileName)
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
        # The file is a JSON string literal (e.g. "C:\\Unity_installs"); parse it so
        # backslashes are unescaped rather than left doubled.
        try { $custom = (Get-Content $secondary -Raw | ConvertFrom-Json) } catch { $custom = "" }
        if (-not [string]::IsNullOrWhiteSpace($custom)) { $roots += $custom }
    }

    # Prefer the exact version folder; otherwise glob for it under each root.
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

$logFile = Join-Path $projectPath "builds/android-build.log"
New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null

# Build the argument list as an array so the optional -aab flag is simply absent
# when not requested (never an empty "" argument passed to Unity).
$unityArgs = @(
    "-quit", "-batchmode", "-nographics",
    "-projectPath", $projectPath,
    "-buildTarget", "Android",
    "-executeMethod", "Fts.EditorTools.MobileBuilder.BuildAndroid",
    "-buildOutput", $Output
)
if ($Aab) { $unityArgs += "-aab" }
$unityArgs += @("-logFile", $logFile)

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $projectPath"
Write-Host "Output:  $Output"
Write-Host ("Building Android {0} (this can take several minutes)..." -f ($(if ($Aab) { "AAB" } else { "APK" }))) -ForegroundColor Cyan
Write-Warning "Close any open Unity Editor on this project first - a second instance will crash the batch build."

# Remove a stale output first so its presence afterwards is a reliable success signal.
if (Test-Path $Output) { Remove-Item $Output -Force }

# Unity.exe is a GUI-subsystem app; the call operator (&) does not reliably surface
# its exit code (it can come back empty even on a successful build). Start-Process
# -Wait -PassThru captures ExitCode properly, and the produced file is the
# source-of-truth check.
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$code = $proc.ExitCode

if (Test-Path $Output) {
    Write-Host "Android build succeeded -> $Output (exit $code)" -ForegroundColor Green
    if (-not $Aab) {
        Write-Host "Install on a connected device:  adb install -r `"$Output`"" -ForegroundColor Green
    }
} else {
    Write-Error "Android build FAILED (exit $code). See log: $logFile"
}
