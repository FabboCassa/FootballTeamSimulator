<#
.SYNOPSIS
    Live smoke test for the ranked daily loop (Phase 9.4) against a running server.

.DESCRIPTION
    Walks the whole 9.4 flow end to end on a real (docker compose) server:
      1. registers a fresh account and joins the ranked ladder
      2. fills the placement cohort with dev bots and ticks the calendar once
         (the season starts AND matchday 1 resolves on that same tick)
      3. reads GET /ranked/today and checks the digest is populated
      4. POSTs /ranked/today/confirm and checks the "confirm matchday" chore is gone
      5. submits a training plan and reads it back

    Needs the dev gates ON in the running server (they are in docker-compose):
      Dev:ExposeSeedEndpoints = true      -> /internal/dev/ranked/fill
      Ranked:ExposeInternalEndpoints=true -> /internal/ranked/tick

.PARAMETER BaseUrl
    Server root. Default http://localhost:8080

.PARAMETER Bots
    How many bot coaches to fill the cohort with. Default 7 (a group of 8 minus you).

.EXAMPLE
    .\tools\smoke-ranked-daily.ps1
    .\tools\smoke-ranked-daily.ps1 -BaseUrl http://localhost:8080 -Bots 7
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8080",
    [int]$Bots = 7
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

$script:Failures = 0

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "== $text" -ForegroundColor Cyan
}

function Write-Ok([string]$text) {
    Write-Host "   OK   $text" -ForegroundColor Green
}

function Write-Bad([string]$text) {
    Write-Host "   FAIL $text" -ForegroundColor Red
    $script:Failures++
}

function Check([bool]$condition, [string]$text) {
    if ($condition) { Write-Ok $text } else { Write-Bad $text }
}

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [hashtable]$Headers,
        $Body
    )
    $uri = "$BaseUrl$Path"
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $Headers -Body $json -ContentType "application/json"
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $Headers
    }
    catch {
        $status = ""
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        throw "$Method $Path failed ($status): $($_.Exception.Message)"
    }
}

Write-Host "Ranked daily-loop smoke (Phase 9.4)" -ForegroundColor White
Write-Host "Server: $BaseUrl"

# ---------------------------------------------------------------- 0. health
Write-Step "Health"
$health = Invoke-Api -Method Get -Path "/health"
Write-Host "   $($health | ConvertTo-Json -Compress)"

# ---------------------------------------------------------------- 1. account
Write-Step "Register a fresh coach"
$email = "smoke_" + [guid]::NewGuid().ToString("N").Substring(0, 10) + "@example.com"
$auth = Invoke-Api -Method Post -Path "/auth/register" -Body @{
    email       = $email
    password    = "Password1"
    displayName = "Smoke Mister"
}
$token = $auth.accessToken
Check ([string]::IsNullOrEmpty($token) -eq $false) "got an access token for $email"
$h = @{ Authorization = "Bearer $token" }

# ---------------------------------------------------------------- 2. ladder
Write-Step "Join the ladder"
$state = Invoke-Api -Method Post -Path "/ranked/enrol" -Headers $h
Check ($state.enrolled -eq $true) "enrolled (world '$($state.worldName)', group '$($state.groupName)')"

Write-Step "Digest BEFORE the season starts"
$today0 = Invoke-Api -Method Get -Path "/ranked/today" -Headers $h
Check ($today0.enrolled -eq $true -and $today0.inSeason -eq $false) "enrolled, not in season yet, $($today0.actionCount) action(s)"

# ---------------------------------------------------------------- 3. dev fill + tick
Write-Step "Fill the cohort with $Bots bots (dev) and tick the calendar"
$fill = Invoke-Api -Method Post -Path "/internal/dev/ranked/fill?count=$Bots"
Write-Host "   fill: $($fill | ConvertTo-Json -Compress)"

$tick = Invoke-Api -Method Post -Path "/internal/ranked/tick"
Write-Host "   tick: $($tick | ConvertTo-Json -Compress)"
Check ($tick.seasonsStarted -ge 1) "a season started"
Check ($tick.matchdaysResolved -ge 1) "matchday 1 resolved on the same tick"

# ---------------------------------------------------------------- 4. the digest
Write-Step "Digest IN SEASON (GET /ranked/today)"
$today = Invoke-Api -Method Get -Path "/ranked/today" -Headers $h

Write-Host "   group      : $($today.groupName) (tier $($today.tier))  club: $($today.clubName)"
Write-Host "   table      : position $($today.yourPosition), $($today.yourPoints) pts, matchday $($today.roundsPlayed)/$($today.totalRounds)"
if ($today.lastResult) {
    $lr = $today.lastResult
    Write-Host "   last result: $($lr.goalsFor)-$($lr.goalsAgainst) vs $($lr.opponentClubName) (home=$($lr.youAreHome))"
}
if ($today.nextMatch) {
    $nm = $today.nextMatch
    $hrs = [math]::Round($nm.secondsToKickoff / 3600.0, 1)
    Write-Host "   next match : round $($nm.round) vs $($nm.opponentClubName) (home=$($nm.youAreHome)) in $hrs h"
}
Write-Host "   inputs     : lineupReady=$($today.lineupReady) lineupConfirmed=$($today.lineupConfirmed) trainingSet=$($today.trainingSet) focus=$($today.trainingTeamFocus)"
Write-Host "   market     : windowOpen=$($null -ne $today.marketWindow) budget=$($today.budget) offersIn=$($today.incomingOffers) lots=$($today.openLots) leading=$($today.lotsYouLead)"
Write-Host "   todo       : $(($today.todo | ForEach-Object { "kind=$($_.kind) count=$($_.count)" }) -join ', ')"

Check ($today.inSeason -eq $true) "in season"
Check ($null -ne $today.nextMatch) "next match reported"
Check ($null -ne $today.lastResult) "last result reported"
Check ($null -ne $today.yourPosition) "table position reported"
Check ($today.lineupReady -eq $true) "SMART DEFAULT: a best-XI lineup was seeded for you"
Check ($today.trainingSet -eq $true) "SMART DEFAULT: a training plan was seeded for you"
Check ($today.lineupConfirmed -eq $false) "the seeded lineup still asks to be confirmed"

$confirmTodo = $today.todo | Where-Object { $_.kind -eq 1 }   # RankedTodoKind.ConfirmMatchday
Check ($null -ne $confirmTodo) "'confirm matchday' is on the to-do list"

# ---------------------------------------------------------------- 5. one-tap confirm
Write-Step "One-tap confirm (POST /ranked/today/confirm)"
$after = Invoke-Api -Method Post -Path "/ranked/today/confirm" -Headers $h
Check ($after.lineupConfirmed -eq $true) "the matchday is confirmed"
$stillThere = $after.todo | Where-Object { $_.kind -eq 1 }
Check ($null -eq $stillThere) "the chore left the to-do list ($($after.actionCount) action(s) left)"

$again = Invoke-Api -Method Post -Path "/ranked/today/confirm" -Headers $h
Check ($again.actionCount -eq $after.actionCount) "a second confirm is a no-op (idempotent)"

# ---------------------------------------------------------------- 6. training
Write-Step "Training plan (POST/GET /ranked/training)"
# NOTE: enums go over as INTEGERS - the API binds plans with the default numeric-enum JSON.
# TeamTrainingFocus: 0 Balanced, 1 Attacking, 2 Defending, 3 Physical, 4 Technical, 5 Tactical.
$null = Invoke-Api -Method Post -Path "/ranked/training" -Headers $h -Body @{
    training = @{
        teamFocus         = 1
        individualFocuses = @{}
    }
}
$stored = Invoke-Api -Method Get -Path "/ranked/training" -Headers $h
Write-Host "   stored plan: $($stored | ConvertTo-Json -Compress)"

$todayAfterTraining = Invoke-Api -Method Get -Path "/ranked/today" -Headers $h
Check ($todayAfterTraining.trainingTeamFocus -eq 1) "the digest reports the new training focus (Attacking)"

# ---------------------------------------------------------------- summary
Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "ALL CHECKS PASSED - the 9.4 daily loop works end to end." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures) CHECK(S) FAILED - see the FAIL lines above." -ForegroundColor Red
exit 1
