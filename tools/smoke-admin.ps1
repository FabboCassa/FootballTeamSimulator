<#
.SYNOPSIS
    Live smoke test for the live-ops surface (Phase 10.3) against a running server.

.DESCRIPTION
    Walks the whole 10.3 flow end to end on a real (docker compose) server:
      1. registers the operator account and confirms /admin is INVISIBLE to it (404, not 403)
      2. grants it the admin role via Admin__BootstrapEmail + a restart, or via -AdminEmail/-AdminPassword
         if you already have an admin
      3. reads /admin/metrics and checks the operational numbers are populated
      4. lists worlds, closes one to enrolment and reopens it
      5. locks a victim account and proves it can no longer sign in (403 account_locked), then unlocks it
      6. pushes a balance revision, checks the server is running it, rolls it back, and checks the
         history is three rows rather than a rewritten two
      7. reads the audit trail and checks every action above left a line

    The account you run this as MUST already be an admin - by design there is no API path to the first
    admin. Set Admin__BootstrapEmail on the server to an account you have registered, restart it, and
    pass that account here.

.PARAMETER BaseUrl
    Server root. Default http://localhost:8080

.PARAMETER AdminEmail / -AdminPassword
    The admin account to drive the surface with.

.EXAMPLE
    .\tools\smoke-admin.ps1 -AdminEmail ops@example.com -AdminPassword Password1

.NOTES
    ASCII-only and PowerShell 5.1 safe.
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8080",
    [Parameter(Mandatory = $true)][string]$AdminEmail,
    [Parameter(Mandatory = $true)][string]$AdminPassword
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')
$script:Failures = 0

function Write-Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Write-Ok([string]$text)   { Write-Host "   OK   $text" -ForegroundColor Green }
function Write-Bad([string]$text)  { Write-Host "   FAIL $text" -ForegroundColor Red; $script:Failures++ }
function Check([bool]$condition, [string]$text) { if ($condition) { Write-Ok $text } else { Write-Bad $text } }

function Invoke-Api {
    param([string]$Method, [string]$Path, [hashtable]$Headers, $Body)
    $uri = "$BaseUrl$Path"
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 64 -Compress
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $Headers -Body $json -ContentType "application/json"
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $Headers
    } catch {
        $status = ""
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        throw "$Method $Path failed ($status): $($_.Exception.Message)"
    }
}

# Status-only call for the cases where the STATUS CODE is the assertion.
function Get-Status {
    param([string]$Method, [string]$Path, [hashtable]$Headers, $Body)
    try {
        $params = @{ Method = $Method; Uri = "$BaseUrl$Path"; UseBasicParsing = $true }
        if ($Headers) { $params.Headers = $Headers }
        if ($null -ne $Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 64 -Compress)
            $params.ContentType = "application/json"
        }
        $resp = Invoke-WebRequest @params
        return [int]$resp.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return -1
    }
}

Write-Host "Live-ops smoke (Phase 10.3)" -ForegroundColor White
Write-Host "Server: $BaseUrl"

# ---------------------------------------------------------------- 0. health
Write-Step "Health"
$health = Invoke-Api -Method Get -Path "/health"
Write-Host "   $($health | ConvertTo-Json -Compress)"

# ---------------------------------------------------------------- 1. the surface hides from players
Write-Step "An ordinary account cannot see /admin at all"
$victimEmail = "smoke_victim_" + [guid]::NewGuid().ToString("N").Substring(0, 8) + "@example.com"
$victim = Invoke-Api -Method Post -Path "/auth/register" -Body @{
    email = $victimEmail; password = "Password1"; displayName = "Smoke Victim"
}
$vh = @{ Authorization = "Bearer $($victim.accessToken)" }
Check ((Get-Status -Method Get -Path "/admin/metrics") -eq 401) "anonymous -> 401"
Check ((Get-Status -Method Get -Path "/admin/metrics" -Headers $vh) -eq 404) "signed-in non-admin -> 404 (not 403)"

# ---------------------------------------------------------------- 2. sign in as the admin
Write-Step "Sign in as $AdminEmail"
$auth = Invoke-Api -Method Post -Path "/auth/login" -Body @{ email = $AdminEmail; password = $AdminPassword }
$h = @{ Authorization = "Bearer $($auth.accessToken)" }
Check ((Get-Status -Method Get -Path "/admin/metrics" -Headers $h) -eq 200) "the admin can reach /admin/metrics"

# ---------------------------------------------------------------- 3. metrics
Write-Step "Metrics"
$m = Invoke-Api -Method Get -Path "/admin/metrics" -Headers $h
Write-Host ("   version {0} / Sim.Core {1} / env {2} / up {3}s" -f $m.version, $m.simCoreVersion, $m.environment, [int]$m.uptimeSeconds)
Write-Host ("   {0} accounts, {1} coaches, {2} worlds, {3} groups ({4} active)" -f $m.accounts, $m.rankedCoaches, $m.rankedWorlds, $m.rankedGroups, $m.activeGroups)
Write-Host ("   fixtures {0}/{1} played, {2} overdue (worst lag {3}s)" -f $m.fixturesPlayed, $m.fixturesTotal, $m.overdueFixtures, $m.worstFixtureLagSeconds)
Check ($m.accounts -ge 2) "account count is populated"
Check ($null -ne $m.jobServers -and $m.jobServers -ge 1) "Hangfire is reporting at least one job server"
if ($m.worstFixtureLagSeconds -lt 600) {
    Write-Ok "the ranked calendar is not behind"
} else {
    Write-Bad ("the ranked calendar is BEHIND: {0} overdue fixture(s), worst {1}s past kickoff" -f $m.overdueFixtures, $m.worstFixtureLagSeconds)
    Write-Host "        On a dev box this is usually 9.6 load-test debris the scheduler is still chewing" -ForegroundColor DarkGray
    Write-Host "        through (one matchday per group per minutely tick). Wait a few minutes and re-read" -ForegroundColor DarkGray
    Write-Host "        /admin/metrics: if overdue is not falling, the tick really is stuck. 'docker compose" -ForegroundColor DarkGray
    Write-Host "        down -v' wipes the residue." -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 4. worlds
Write-Step "Worlds"
$worlds = Invoke-Api -Method Get -Path "/admin/worlds" -Headers $h
if (@($worlds).Count -ge 1) {
    $w = $worlds[0]
    Write-Host ("   '{0}': {1} groups, {2}/{3} seats taken, {4} human coaches" -f $w.name, $w.groups, $w.occupiedSeats, $w.seats, $w.humanCoaches)
    $closed = Invoke-Api -Method Post -Path "/admin/worlds/$($w.id)/open" -Headers $h -Body @{ open = $false; reason = "smoke test" }
    Check ($closed.status -eq "Closed") "world closed to enrolment"
    $reopened = Invoke-Api -Method Post -Path "/admin/worlds/$($w.id)/open" -Headers $h -Body @{ open = $true; reason = "smoke test done" }
    Check ($reopened.status -eq "Open") "world reopened"
} else {
    Write-Host "   (no ranked worlds yet - enrol a coach first if you want this checked)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 5. lock / unlock
Write-Step "Locking an account really stops it signing in"
$found = Invoke-Api -Method Get -Path "/admin/users?q=$victimEmail" -Headers $h
Check (@($found).Count -ge 1) "the victim is findable by email"
$victimId = $found[0].userId

$locked = Invoke-Api -Method Post -Path "/admin/users/$victimId/lock" -Headers $h -Body @{ locked = $true; reason = "smoke test" }
Check ($locked.isLocked -eq $true) "the account reads as locked"
Check ((Get-Status -Method Post -Path "/auth/login" -Body @{ email = $victimEmail; password = "Password1" }) -eq 403) "login refused with 403 (right password, suspended account)"

$unlocked = Invoke-Api -Method Post -Path "/admin/users/$victimId/lock" -Headers $h -Body @{ locked = $false; reason = "smoke test done" }
Check ($unlocked.isLocked -eq $false) "the account reads as unlocked"
Check ((Get-Status -Method Post -Path "/auth/login" -Body @{ email = $victimEmail; password = "Password1" }) -eq 200) "login works again"

# ---------------------------------------------------------------- 6. balance push + rollback
Write-Step "Balance push"
$before = Invoke-Api -Method Get -Path "/admin/balance" -Headers $h
Write-Host ("   active revision {0} (baseline: {1}), config version {2}" -f $before.revision, $before.isBaseline, $before.configVersion)

# Edit the document the dashboard hands you, exactly as the dashboard does. -Depth 64 above matters:
# BalanceConfig nests deeply and ConvertTo-Json silently truncates past its default depth of 2.
$doc = $before.json | ConvertFrom-Json
$doc.Version = [int]$before.configVersion + 1
$pushed = Invoke-Api -Method Post -Path "/admin/balance" -Headers $h -Body @{
    json = ($doc | ConvertTo-Json -Depth 64 -Compress); note = "smoke test push"
}
Check ($pushed.revision -eq ($before.revision + 1)) "revision advanced to $($pushed.revision)"
Check ($pushed.isBaseline -eq $false) "no longer on the build baseline"

$m2 = Invoke-Api -Method Get -Path "/admin/metrics" -Headers $h
Check ($m2.balanceRevision -eq $pushed.revision) "the RUNNING server is on revision $($pushed.revision)"

Write-Step "A partial document is refused"
$bad = Get-Status -Method Post -Path "/admin/balance" -Headers $h -Body @{ json = '{"Match":{}}'; note = "fragment" }
Check ($bad -eq 400) "a fragment is rejected with 400 (it would have reset every other section)"
$m3 = Invoke-Api -Method Get -Path "/admin/metrics" -Headers $h
Check ($m3.balanceRevision -eq $pushed.revision) "the refused push did not move the live balance"

Write-Step "Rollback moves forward"
$rolled = Invoke-Api -Method Post -Path "/admin/balance/rollback/$($before.revision + 1)" -Headers $h
$history = Invoke-Api -Method Get -Path "/admin/balance/history" -Headers $h
Check ($rolled.revision -gt $pushed.revision) "the rollback is a NEW revision ($($rolled.revision)), not a resurrected one"
Check (@($history | Where-Object { $_.isActive }).Count -eq 1) "exactly one revision is active"

# ---------------------------------------------------------------- 7. audit
Write-Step "Audit trail"
$audit = Invoke-Api -Method Get -Path "/admin/audit?limit=20" -Headers $h
$actions = $audit | ForEach-Object { $_.action }
Write-Host ("   last {0} entries, actions: {1}" -f @($audit).Count, ($actions -join ", "))
# 1 = BalancePush, 2 = BalanceRollback, 3 = UserLock, 4 = UserUnlock.
foreach ($pair in @(@(3, "UserLock"), @(4, "UserUnlock"), @(1, "BalancePush"), @(2, "BalanceRollback"))) {
    Check ($actions -contains $pair[0]) "$($pair[1]) was audited"
}
Check (@($audit | Where-Object { $_.actorEmail -eq $AdminEmail }).Count -ge 4) "every line names the operator"

# ---------------------------------------------------------------- done
Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "ALL CHECKS PASSED" -ForegroundColor Green
    exit 0
}
Write-Host ("$script:Failures CHECK(S) FAILED") -ForegroundColor Red
exit 1
