# Builds Sim.Core as it was at an older commit, for in-process comparisons such as
# EngineTimingHarnessTests (real-match spec R15: v12 generation time vs v11 in the same run).
# Default commit e3992bc is engine v11 on main just before Phase A. Prints the DLL path.
# Run from anywhere: .\tools\build-simcore-reference.ps1 [-Commit <sha>]
param([string]$Commit = "e3992bc")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([System.IO.Path]::GetTempPath()) "fts-simcore-$Commit"
$zip = "$work.zip"

if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force -Path $work | Out-Null
git -C $root archive --format=zip -o $zip $Commit shared/Sim.Core
if ($LASTEXITCODE -ne 0) { throw "git archive of $Commit failed" }
Expand-Archive -Path $zip -DestinationPath $work
Remove-Item $zip
dotnet build "$work/shared/Sim.Core/Sim.Core.csproj" -c Release -o "$work/out" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build of $Commit failed" }

Write-Output (Join-Path $work "out/Sim.Core.dll")
