<#
.SYNOPSIS
    Poll a running FTS server and alert when something is actually wrong (Roadmap 10.3).

.DESCRIPTION
    Signs in as an admin account, reads GET /health and GET /admin/metrics, and judges four things.
    They are the four ways this server has failed or could fail, and nothing else - a monitor that
    reports everything is a monitor nobody reads:

      1. UNREACHABLE       - /health does not answer. Everything else is moot.
      2. CALENDAR BEHIND   - WorstFixtureLagSeconds over the threshold. The ranked calendar resolves a
                             matchday within a minute of kickoff, so a lag past a few minutes means the
                             Hangfire worker is starved, stopped, or the tick is throwing. This is the
                             one that silently ruins a season, because the API stays perfectly healthy
                             while the ladder stops moving. The 9.6 load test measured the job at 5.0s
                             for 500 fixtures, so the default 600s threshold is 120x its measured cost.
      3. JOBS FAILING      - Hangfire's failed count climbing.
      4. BALANCE SKEW      - the instance answering is not on the newest revision (only meaningful with
                             more than one instance; the reload job should close this within a minute).

    Exit code is 0 when everything is fine and 1 when anything alerted, so a scheduled task, a cron job
    or a CI step can act on it without parsing the output.

    -Once runs a single check (that is the mode a scheduler wants). Without it the script loops.

.PARAMETER Webhook
    Optional. A URL that gets a JSON POST { text = "..." } per alert - Slack/Discord shaped, which is
    what most things accept. Left empty the script just prints and sets its exit code.

.EXAMPLE
    .\tools\watchdog.ps1 -Email ops@example.com -Password $env:FTS_ADMIN_PW -Once
    .\tools\watchdog.ps1 -BaseUrl https://api.example.com -Email ... -Password ... -IntervalSeconds 300

.NOTES
    ASCII-only and PowerShell 5.1 safe.
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8080",
    [Parameter(Mandatory = $true)][string]$Email,
    [Parameter(Mandatory = $true)][string]$Password,
    [int]$MaxFixtureLagSeconds = 600,
    [int]$MaxFailedJobs = 0,
    [int]$IntervalSeconds = 300,
    [switch]$Once,
    [string]$Webhook = ""
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

$script:Alerts = 0

function Write-Ok([string]$text)   { Write-Host ("   OK    " + $text) -ForegroundColor Green }
function Write-Info([string]$text) { Write-Host ("         " + $text) -ForegroundColor DarkGray }

function Raise([string]$text) {
    Write-Host ("   ALERT " + $text) -ForegroundColor Red
    $script:Alerts++
    if ($Webhook) {
        try {
            $body = @{ text = ("[FTS watchdog] " + $text) } | ConvertTo-Json -Compress
            Invoke-RestMethod -Method Post -Uri $Webhook -ContentType "application/json" -Body $body | Out-Null
        } catch {
            Write-Host ("         (webhook failed: " + $_.Exception.Message + ")") -ForegroundColor DarkYellow
        }
    }
}

function Get-AdminToken {
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json -Compress
    $auth = Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/login" -ContentType "application/json" -Body $body
    return $auth.accessToken
}

function Invoke-Check {
    $script:Alerts = 0
    Write-Host ""
    Write-Host ("== " + (Get-Date -Format "u") + "  " + $BaseUrl) -ForegroundColor Cyan

    # 1. reachable at all
    try {
        $health = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health" -TimeoutSec 15
        Write-Ok ("reachable - version {0}, Sim.Core {1}" -f $health.version, $health.simCore)
    } catch {
        Raise ("server UNREACHABLE: " + $_.Exception.Message)
        return
    }

    try {
        $token = Get-AdminToken
        $headers = @{ Authorization = "Bearer $token" }
        $m = Invoke-RestMethod -Method Get -Uri "$BaseUrl/admin/metrics" -Headers $headers -TimeoutSec 30
    } catch {
        # A 404 here is the admin role missing, not a missing endpoint - the surface hides itself.
        Raise ("could not read /admin/metrics (is this account an admin?): " + $_.Exception.Message)
        return
    }

    Write-Info ("uptime {0}s, {1} accounts ({2} new today), {3} coaches on {4} worlds" -f `
        [int]$m.uptimeSeconds, $m.accounts, $m.accountsCreated24h, $m.rankedCoaches, $m.rankedWorlds)

    # 2. the calendar
    if ($m.worstFixtureLagSeconds -gt $MaxFixtureLagSeconds) {
        Raise ("calendar BEHIND: {0} overdue fixture(s), worst {1}s past kickoff (threshold {2}s)" -f `
            $m.overdueFixtures, $m.worstFixtureLagSeconds, $MaxFixtureLagSeconds)
    } else {
        Write-Ok ("calendar on time ({0} overdue, worst {1}s)" -f $m.overdueFixtures, $m.worstFixtureLagSeconds)
    }

    # 3. the scheduler
    if ($null -eq $m.failedJobs) {
        Write-Info "background jobs: not reporting (scheduler disabled in this environment)"
    } elseif ($m.failedJobs -gt $MaxFailedJobs) {
        Raise ("{0} FAILED background job(s) (threshold {1})" -f $m.failedJobs, $MaxFailedJobs)
    } elseif ($m.jobServers -lt 1) {
        Raise "no Hangfire server is registered - nothing is running the ranked calendar"
    } else {
        Write-Ok ("scheduler healthy ({0} server(s), {1} enqueued, {2} failed)" -f `
            $m.jobServers, $m.enqueuedJobs, $m.failedJobs)
    }

    # 4. balance skew
    try {
        $balance = Invoke-RestMethod -Method Get -Uri "$BaseUrl/admin/balance" -Headers $headers -TimeoutSec 30
        if ($balance.revision -ne $m.balanceRevision) {
            Raise ("balance SKEW: the stored revision is {0} but the instance that answered is on {1}" -f `
                $balance.revision, $m.balanceRevision)
        } else {
            Write-Ok ("balance revision {0}" -f $m.balanceRevision)
        }
    } catch {
        Write-Info ("balance check skipped: " + $_.Exception.Message)
    }

    if ($script:Alerts -eq 0) { Write-Host "   -- all clear" -ForegroundColor Green }
}

if ($Once) {
    Invoke-Check
    exit ([int]($script:Alerts -gt 0))
}

Write-Host "Watchdog running every $IntervalSeconds s. Ctrl+C to stop." -ForegroundColor Yellow
while ($true) {
    try { Invoke-Check } catch { Raise ("watchdog itself threw: " + $_.Exception.Message) }
    Start-Sleep -Seconds $IntervalSeconds
}
