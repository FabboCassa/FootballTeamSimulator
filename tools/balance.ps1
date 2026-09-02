<#
.SYNOPSIS
    Balance harness (Phase 10.1) - measures the game's balance by simulating it.

.DESCRIPTION
    Builds and runs tools/BalanceHarness, which measures five things and prints a
    PASS/FAIL block (exit code 0 only if every check passes):

      tactics     the 81-combo instruction field and the 6-shape formation field played
                  round-robin on equal squads with the LIVE engine flags, plus what the
                  best tactic is worth to a mid club over full seasons.
      economy     value distribution, club kitties, transfer volume and fees, wages vs
                  income, and how much of a ranked world a 25M kitty can actually buy.
      difficulty  Easy/Normal/Hard over full seasons with identical user behaviour.
      ladder      the ranked pyramid driven by the server's own Elo model, swept over
                  seat occupancy (promotion needs a FREE seat, so a full pyramid can
                  freeze - that is measured, not assumed).
      world       task 11.1's database presets: generation time, club/player counts,
                  managed heap, save size (raw and gzipped) and the cost of a whole
                  world season at Small / Medium / Large, plus how closely the cheap
                  background resolver tracks the real match engine.

    Plus one that is NOT part of "all", because it measures a rewrite in progress rather
    than the shipped balance:

      pitch       what the match LOOKS like: goals, shots, passes and their accuracy,
                  restarts, ground covered, and the SHAPE of each block - width, depth,
                  how near the back four is to being a line, how much of the match a
                  player spends inside three metres of an opponent. Every reading is
                  printed against the band real football produces. Phase 0 of the match
                  engine rework; see docs/engine/MATCH_ENGINE_PLAN.md.

    Nothing here talks to a server or a database: it runs Sim.Core and the server's
    rating maths in-process, so it is safe to run any time and it replays exactly for
    a given seed.

.PARAMETER Scenario
    all | tactics | economy | difficulty | ladder | world | pitch. Default all.
    "pitch" is NOT included in "all" - ask for it by name.

.PARAMETER PitchMatches
    Matches measured by the pitch scenario. Default 100 (the harness default).

.PARAMETER PitchStrict
    Turn the pitch scenario's "against real football" bands into PASS/FAIL checks.

.PARAMETER PitchDump
    Write one measured match out as a self-contained HTML replay at this path.

.PARAMETER Seed
    Root seed. Default 20260803. Same seed = same numbers.

.PARAMETER Long
    The roadmap-sized run: more repeats and more seasons, tighter numbers, slower.

.EXAMPLE
    .\tools\balance.ps1
    .\tools\balance.ps1 -Scenario tactics
    .\tools\balance.ps1 -Long
    .\tools\balance.ps1 -Scenario pitch
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html
#>

[CmdletBinding()]
param(
    [ValidateSet("all", "tactics", "economy", "difficulty", "ladder", "world", "pitch")]
    [string]$Scenario = "all",
    [long]$Seed = 20260803,
    [switch]$Long,
    [int]$PitchMatches = 0,
    [switch]$PitchStrict,
    [string]$PitchDump
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "BalanceHarness\BalanceHarness.csproj"

if (-not (Test-Path $project)) {
    Write-Host "Balance harness project not found at $project" -ForegroundColor Red
    exit 2
}

Write-Host ""
Write-Host "Building the balance harness..." -ForegroundColor Cyan
& dotnet build $project -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit 2
}

$arguments = @(
    "run", "-c", "Release", "--no-build", "--project", $project, "--",
    "--scenario", $Scenario,
    "--seed", $Seed
)
if ($Long) { $arguments += "--long" }
if ($PitchMatches -gt 0) { $arguments += @("--pitch-matches", $PitchMatches) }
if ($PitchStrict) { $arguments += "--pitch-strict" }
if ($PitchDump) { $arguments += @("--pitch-dump", $PitchDump) }

Write-Host ""
Write-Host "Running: dotnet $($arguments -join ' ')" -ForegroundColor Cyan
Write-Host "(a default run is minutes of simulation; -Long is longer)"
Write-Host ""

& dotnet $arguments | Out-Host
$code = $LASTEXITCODE

Write-Host ""
if ($code -eq 0) {
    Write-Host "Balance checks PASSED" -ForegroundColor Green
} else {
    Write-Host "Balance checks FAILED (exit $code)" -ForegroundColor Red
    Write-Host "Read the [balance-*] lines above before changing anything: a failed check is a" -ForegroundColor Yellow
    Write-Host "measurement, and which knob to turn is a decision to take together." -ForegroundColor Yellow
}

exit $code
