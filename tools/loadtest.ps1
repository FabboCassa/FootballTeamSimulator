<#
.SYNOPSIS
    Load test for the ranked ladder (Phase 9.6) against a running server.

.DESCRIPTION
    Builds and runs tools/LoadTest, which:
      1. seeds a cohort of real accounts through /internal/dev/ranked/load-seed
         (register + enrol + the calendar ticks that start their seasons)
      2. runs the steady daily loop with every coach concurrent (digest, season,
         offers, one-tap confirm)
      3. runs the auction spike and audits every accepted bid for losses
      4. resolves the ladder's matchday while everyone refreshes, and times the job

    Checks (exit code 0 only if all pass):
      p95 latency under budget in each phase, no 5xx, no dropped connections,
      no lost bids, matchday job inside its window.

    Needs the dev gates ON in the running server (docker compose already sets both):
      Dev:ExposeSeedEndpoints       = true  -> /internal/dev/ranked/load-seed
      Ranked:ExposeInternalEndpoints= true  -> /internal/ranked/fast-forward

    NOTE: on a development machine the generator, the API, PostgreSQL and Redis all
    share the same CPU. Read the numbers as a regression baseline; the absolute p95
    has to be re-confirmed on a real host before launch.

.PARAMETER BaseUrl
    Server root. Default http://localhost:8080

.PARAMETER Users
    Concurrent virtual coaches. Default 200. The roadmap's target run is 1000.

.PARAMETER Coaches
    Accounts to seed. Defaults to Users.

.PARAMETER Seconds
    Steady daily-loop phase, in seconds. Default 60.

.PARAMETER AuctionSeconds
    Auction spike, in seconds. Default 30.

.PARAMETER Scenario
    all | baseline | auction | matchday | tick. Default all.
    'tick' is the isolation run: it seeds the cohort and then times the
    matchday job on an idle server, so a slow result under load can be told
    apart from a job that is slow in itself.

.PARAMETER P95
    Latency budget in milliseconds. Default 300.

.PARAMETER MatchdayBudget
    Matchday job budget in seconds. Default 60 (the recurring job's cadence).

.EXAMPLE
    .\tools\loadtest.ps1
    .\tools\loadtest.ps1 -Users 1000 -Seconds 120
    .\tools\loadtest.ps1 -Scenario auction -Users 400 -AuctionSeconds 60
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8080",
    [int]$Users = 200,
    [int]$Coaches = 0,
    [int]$Seconds = 60,
    [int]$AuctionSeconds = 30,
    [ValidateSet("all", "baseline", "auction", "matchday", "tick")]
    [string]$Scenario = "all",
    [int]$P95 = 300,
    [int]$MatchdayBudget = 60
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot "LoadTest\LoadTest.csproj"

if (-not (Test-Path $project)) {
    Write-Host "Load test project not found at $project" -ForegroundColor Red
    exit 2
}

if ($Coaches -le 0) { $Coaches = $Users }

Write-Host ""
Write-Host "Building the load generator..." -ForegroundColor Cyan
& dotnet build $project -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit 2
}

$arguments = @(
    "run", "-c", "Release", "--no-build", "--project", $project, "--",
    "--url", $BaseUrl,
    "--users", $Users,
    "--coaches", $Coaches,
    "--seconds", $Seconds,
    "--auction-seconds", $AuctionSeconds,
    "--scenario", $Scenario,
    "--p95", $P95,
    "--matchday-budget", $MatchdayBudget
)

Write-Host ""
Write-Host "Running: dotnet $($arguments -join ' ')" -ForegroundColor Cyan
Write-Host ""

& dotnet $arguments | Out-Host
$code = $LASTEXITCODE

Write-Host ""
if ($code -eq 0) {
    Write-Host "Load test PASSED" -ForegroundColor Green
} else {
    Write-Host "Load test FAILED (exit $code)" -ForegroundColor Red
    Write-Host "Tip: a big cohort leaves a lot of rows behind. To start clean:" -ForegroundColor Yellow
    Write-Host "     cd $root\server; docker compose down -v; docker compose up -d --build" -ForegroundColor Yellow
}

exit $code
