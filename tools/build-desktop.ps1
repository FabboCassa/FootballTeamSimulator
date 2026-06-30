# Builds the Unity desktop / Steam-ready player(s) headlessly (Roadmap 6.5).
#   .\tools\build-desktop.ps1                      # Windows x64, auto-detect Unity 6000.3.17f1
#   .\tools\build-desktop.ps1 -Platform Mac        # macOS .app
#   .\tools\build-desktop.ps1 -Platform Linux      # Linux x64
#   .\tools\build-desktop.ps1 -Platform All        # Windows + Mac + Linux
#   .\tools\build-desktop.ps1 -UnityPath "C:\Path\To\Unity.exe"
#
# Requires:
#   * the Boot scene enabled in File > Build Settings;
#   * the matching platform module installed in Unity Hub (Windows Build Support
#     ships with the editor; "Mac Build Support (Mono)" and "Linux Build Support
#     (IL2CPP)" must be added for those targets).
#
# Output goes to client/builds/<platform> (gitignored). On Windows, double-click
# client\builds\windows\FootballTeamSimulator.exe to run.
#
# "Steam-ready" packaging only: a clean standalone build. The Steamworks SDK
# integration (overlay/achievements/cloud) is a later step once an App ID exists.
param(
    [ValidateSet("Windows", "Mac", "Linux", "All")]
    [string]$Platform = "Windows",
    [string]$UnityPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root "client"

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

# Per-platform: the Unity build target, the -executeMethod, and the output file.
$matrix = @{
    Windows = @{ Target = "StandaloneWindows64"; Method = "Fts.EditorTools.DesktopBuilder.BuildWindows"; Output = "builds/windows/FootballTeamSimulator.exe" }
    Mac     = @{ Target = "StandaloneOSX";        Method = "Fts.EditorTools.DesktopBuilder.BuildMac";     Output = "builds/mac/FootballTeamSimulator.app" }
    Linux   = @{ Target = "StandaloneLinux64";    Method = "Fts.EditorTools.DesktopBuilder.BuildLinux";   Output = "builds/linux/FootballTeamSimulator.x86_64" }
}

$targets = if ($Platform -eq "All") { @("Windows", "Mac", "Linux") } else { @($Platform) }

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $projectPath"
Write-Warning "Close any open Unity Editor on this project first - a second instance will crash the batch build."

$failures = @()
foreach ($p in $targets) {
    $cfg = $matrix[$p]
    $output = Join-Path $projectPath $cfg.Output
    $logFile = Join-Path $projectPath ("builds/{0}-build.log" -f $p.ToLower())
    New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null

    Write-Host ""
    Write-Host ("Building {0} (this can take several minutes)..." -f $p) -ForegroundColor Cyan
    Write-Host "Output:  $output"

    # Remove a stale output first so its presence afterwards is a reliable success signal.
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }

    $unityArgs = @(
        "-quit", "-batchmode", "-nographics",
        "-projectPath", $projectPath,
        "-buildTarget", $cfg.Target,
        "-executeMethod", $cfg.Method,
        "-buildOutput", $output,
        "-logFile", $logFile
    )

    # Unity.exe is a GUI-subsystem app; the call operator (&) does not reliably surface
    # its exit code. Start-Process -Wait -PassThru captures ExitCode properly, and the
    # produced file is the source-of-truth check.
    $proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
    $code = $proc.ExitCode

    if (Test-Path $output) {
        Write-Host ("{0} build succeeded -> {1} (exit {2})" -f $p, $output, $code) -ForegroundColor Green
    } else {
        Write-Warning ("{0} build FAILED (exit {1}). See log: {2}" -f $p, $code, $logFile)
        $failures += $p
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Error ("Desktop build FAILED for: {0}" -f ($failures -join ", "))
} else {
    Write-Host "All requested desktop build(s) succeeded." -ForegroundColor Green
}
