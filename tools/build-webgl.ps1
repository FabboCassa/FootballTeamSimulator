# Builds the Unity WebGL player headlessly (Roadmap 6.3).
#   .\tools\build-webgl.ps1                  # auto-detect Unity 6000.3.17f1
#   .\tools\build-webgl.ps1 -UnityPath "C:\Path\To\Unity.exe"
#   .\tools\build-webgl.ps1 -Output "C:\out\webgl"
#
# Requires the Boot scene to be enabled in File > Build Settings.
# Output goes to client/builds/webgl (gitignored). After it finishes, serve it
# over HTTP (NOT file://) — e.g.  python -m http.server 8000  inside the output
# folder — and open it in Chrome/Firefox/Safari.
param(
    [string]$UnityPath = "",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root "client"

if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path $projectPath "builds/webgl"
}

# Resolve the editor version from the project so we use the matching Unity.
$version = (Get-Content (Join-Path $projectPath "ProjectSettings/ProjectVersion.txt") |
    Select-String "m_EditorVersion:").ToString().Split(":")[1].Trim()

if ([string]::IsNullOrEmpty($UnityPath)) {
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "$env:LOCALAPPDATA\Programs\Unity\Hub\Editor\$version\Editor\Unity.exe"
    )
    $UnityPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrEmpty($UnityPath) -or -not (Test-Path $UnityPath)) {
    Write-Error "Unity $version not found. Pass -UnityPath 'C:\...\Unity.exe' explicitly."
}

$logFile = Join-Path $projectPath "builds/webgl-build.log"
New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $projectPath"
Write-Host "Output:  $Output"
Write-Host "Building WebGL (this can take several minutes)..." -ForegroundColor Cyan

& $UnityPath -quit -batchmode -nographics `
    -projectPath $projectPath `
    -buildTarget WebGL `
    -executeMethod Fts.EditorTools.WebGLBuilder.Build `
    -buildOutput $Output `
    -logFile $logFile

$code = $LASTEXITCODE
if ($code -eq 0) {
    Write-Host "WebGL build succeeded → $Output" -ForegroundColor Green
    Write-Host "Serve it over HTTP, e.g.:  cd `"$Output`"; python -m http.server 8000" -ForegroundColor Green
} else {
    Write-Error "WebGL build FAILED (exit $code). See log: $logFile"
}
