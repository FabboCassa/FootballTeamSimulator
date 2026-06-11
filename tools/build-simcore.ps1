# Builds Sim.Core + Contracts and copies the DLLs into the Unity project.
# Run from anywhere: .\tools\build-simcore.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "client/Assets/Plugins/SimCore"

dotnet build "$root/shared/Sim.Core/Sim.Core.csproj" -c Release
dotnet build "$root/shared/Contracts/Contracts.csproj" -c Release

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$root/shared/Sim.Core/bin/Release/netstandard2.1/Sim.Core.dll" $dest -Force
Copy-Item "$root/shared/Sim.Core/bin/Release/netstandard2.1/Sim.Core.pdb" $dest -Force -ErrorAction SilentlyContinue
Copy-Item "$root/shared/Contracts/bin/Release/netstandard2.1/Fts.Contracts.dll" $dest -Force
Copy-Item "$root/shared/Contracts/bin/Release/netstandard2.1/Fts.Contracts.pdb" $dest -Force -ErrorAction SilentlyContinue

Write-Host "Done. DLLs copied to client/Assets/Plugins/SimCore/" -ForegroundColor Green
