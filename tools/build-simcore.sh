#!/usr/bin/env bash
# Builds Sim.Core + Contracts and copies the DLLs into the Unity project.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/client/Assets/Plugins/SimCore"

dotnet build "$ROOT/shared/Sim.Core/Sim.Core.csproj" -c Release
dotnet build "$ROOT/shared/Contracts/Contracts.csproj" -c Release

mkdir -p "$DEST"
cp "$ROOT/shared/Sim.Core/bin/Release/netstandard2.1/Sim.Core.dll" "$DEST/"
cp "$ROOT/shared/Contracts/bin/Release/netstandard2.1/Fts.Contracts.dll" "$DEST/"
cp "$ROOT/shared/Sim.Core/bin/Release/netstandard2.1/Sim.Core.pdb" "$DEST/" 2>/dev/null || true
cp "$ROOT/shared/Contracts/bin/Release/netstandard2.1/Fts.Contracts.pdb" "$DEST/" 2>/dev/null || true

echo "Done. DLLs copied to client/Assets/Plugins/SimCore/"
