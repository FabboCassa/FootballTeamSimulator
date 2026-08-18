<#
.SYNOPSIS
    Launch preflight (Roadmap 10.4): decide whether a deployed FTS server is fit to submit and open.

.DESCRIPTION
    Section 1 of docs/store/release-checklist.md is the set of blockers that apply to EVERY store -
    the ones where a submission is refused, or where opening would be irresponsible. Most of them were
    written as things to remember. This script turns the ones a machine can settle into assertions
    against the running deployment, so "we checked" stops being a memory:

      1.  TLS        - the base URL is https, the certificate validates, http redirects to it, and the
                       response carries HSTS. Without this no store form can claim encryption in transit.
      2.  IDENTITY   - /health answers, reports the version being shipped (tools/version.json), and says
                       which ROLE this instance is. Behind the public name that must be api or both; if it
                       says worker, the proxy is pointed at the scheduler.
      3.  READINESS  - /health/ready is Healthy, i.e. PostgreSQL is migrated and Redis answers.
      4.  DEV DOORS  - /internal/dev/*, /internal/ranked/*, /internal/sim/*, /internal/jobs/* and
                       /hangfire are all 404. The dev seeding endpoints are UNAUTHENTICATED: one of them
                       reachable in production is a full compromise of the ladder, not a nuisance.
                       It PROBES rather than invokes - the mutating routes are asked with a verb they are
                       not mapped for, so 405 means the route is there and the body never ran. Running
                       this against a dev stack is therefore safe.
      5.  SECRETS    - THE ONE WORTH THE SCRIPT. It mints a token signed with each signing key that ships
                       in the repository and offers it to /auth/me. A 401 means the server rejected it,
                       so it is NOT running a shipped key. Anything else means anyone holding this
                       repository can mint an admin's token, and the launch stops here.
      6.  ADMIN      - /admin/metrics is 401 anonymously (the surface exists but takes no orders), and
                       with -AdminEmail/-AdminPassword it is read for the two facts only the server
                       knows: the environment really is Production, and a Hangfire server is registered
                       even though THIS instance is role api - which is the proof that the worker is a
                       separate process (10.4) rather than absent.
      7.  CORS       - with -WebOrigin, a request carrying that Origin comes back allowed. The public
                       account-deletion page is a browser form calling this API; if CORS is not set the
                       page silently cannot submit, and account deletion is a store blocker.
      8.  LOCAL      - with -EnvFile, the production env file still holding CHANGE_ME placeholders or
                       being visible to git is a failure. With -BackupPath, the newest database archive
                       must be younger than -MaxBackupAgeHours: an untested launch with no backup is the
                       definition of irresponsible.

    Exit code is 0 when every check passed and 1 otherwise, so this can gate a release pipeline.
    Anything the script cannot settle by itself is printed at the end as the MANUAL list - it does not
    pretend those are done.

.PARAMETER BaseUrl
    The public API root, e.g. https://api.example.com

.PARAMETER AllowHttp
    Skip the TLS assertions. For rehearsing the script against the local stack ONLY - a real preflight
    that needs this flag has failed check 1.

.EXAMPLE
    .\tools\preflight-launch.ps1 -BaseUrl https://api.example.com
    .\tools\preflight-launch.ps1 -BaseUrl https://api.example.com -AdminEmail ops@example.com `
        -AdminPassword $env:FTS_ADMIN_PW -WebOrigin https://play.example.com `
        -EnvFile .\server\.env.prod -BackupPath .\backups

.NOTES
    ASCII-only and PowerShell 5.1 safe. READ-ONLY: it registers nothing, enqueues nothing and executes
    no endpoint body - safe to rehearse against a dev stack. (It was not, in its first version; see the
    comment above section 4.)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$AdminEmail = "",
    [string]$AdminPassword = "",
    [string]$WebOrigin = "",
    [string]$EnvFile = "",
    [string]$BackupPath = "",
    [int]$MaxBackupAgeHours = 48,
    [int]$MaxFixtureLagSeconds = 600,
    [switch]$AllowHttp
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

# PowerShell 5.1 still negotiates TLS 1.0 by default on some hosts, which a modern server refuses -
# that failure would look like a broken deployment rather than a client default.
try {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls11
} catch { }

$script:Failures = 0
$script:Warnings = 0
$script:Manual = New-Object System.Collections.ArrayList

function Write-Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Write-Ok([string]$text)   { Write-Host "   OK   $text" -ForegroundColor Green }
function Write-Bad([string]$text)  { Write-Host "   FAIL $text" -ForegroundColor Red; $script:Failures++ }
function Write-Warn([string]$text) { Write-Host "   WARN $text" -ForegroundColor Yellow; $script:Warnings++ }
function Write-Info([string]$text) { Write-Host "        $text" -ForegroundColor DarkGray }
function Check([bool]$condition, [string]$text) { if ($condition) { Write-Ok $text } else { Write-Bad $text } }
function Manual([string]$text) { [void]$script:Manual.Add($text) }

# A request whose STATUS CODE is the assertion. Never throws: an unreachable host returns -1 so the
# caller can tell "refused" from "answered 404".
function Get-Status {
    param([string]$Method = "Get", [string]$Uri, [hashtable]$Headers, $Body, [int]$TimeoutSec = 20)
    try {
        $params = @{ Method = $Method; Uri = $Uri; UseBasicParsing = $true; TimeoutSec = $TimeoutSec }
        if ($Headers) { $params.Headers = $Headers }
        if ($null -ne $Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 32 -Compress)
            $params.ContentType = "application/json"
        }
        $resp = Invoke-WebRequest @params
        return [int]$resp.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return -1
    }
}

# Reading one response header portably. PowerShell 5.1 hands back a Dictionary[string,string] and 7 a
# collection of string[], and indexing a key that is absent is not safe on either - so walk the keys.
function Get-Header {
    param($Response, [string]$Name)
    if ($null -eq $Response -or $null -eq $Response.Headers) { return $null }
    foreach ($key in $Response.Headers.Keys) {
        if ($key -eq $Name) { return (($Response.Headers[$key]) -join ", ") }
    }
    return $null
}

function Get-Json {
    param([string]$Method = "Get", [string]$Uri, [hashtable]$Headers, $Body, [int]$TimeoutSec = 30)
    $params = @{ Method = $Method; Uri = $Uri; TimeoutSec = $TimeoutSec }
    if ($Headers) { $params.Headers = $Headers }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 32 -Compress)
        $params.ContentType = "application/json"
    }
    return Invoke-RestMethod @params
}

# --- the forged-token check ------------------------------------------------------------------------
# Builds a real HS256 JWT with the given key and the issuer/audience the server validates. If the server
# accepts it, that key is the live signing key. The subject is a random GUID belonging to no account, so
# even a server that DOES accept it cannot be made to do anything - the point is only whether the
# signature is honoured.
function Convert-ToBase64Url([byte[]]$bytes) {
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-ForgedToken([string]$key) {
    $enc = [Text.Encoding]::UTF8
    $header = '{"alg":"HS256","typ":"JWT"}'
    # Epoch by subtraction rather than -UFormat %s: the latter is culture- and timezone-dependent on
    # PowerShell 5.1, and a token that is merely EXPIRED would be rejected for the wrong reason and read
    # here as "the key is not live" - a false pass on the one check that must not have one.
    $epoch = New-Object DateTime 1970, 1, 1, 0, 0, 0, ([DateTimeKind]::Utc)
    $now = [int][math]::Floor(([DateTime]::UtcNow - $epoch).TotalSeconds)
    $payload = '{"sub":"' + [guid]::NewGuid().ToString() + '","iss":"fts","aud":"fts-client","nbf":' `
             + $now + ',"exp":' + ($now + 300) + '}'

    $signingInput = (Convert-ToBase64Url $enc.GetBytes($header)) + "." + (Convert-ToBase64Url $enc.GetBytes($payload))
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = $enc.GetBytes($key)
    $signature = Convert-ToBase64Url $hmac.ComputeHash($enc.GetBytes($signingInput))
    $hmac.Dispose()
    return "$signingInput.$signature"
}

Write-Host "FTS launch preflight (Roadmap 10.4)" -ForegroundColor White
Write-Host "Target: $BaseUrl"

# ---------------------------------------------------------------------------- 1. TLS
Write-Step "1. TLS"
if ($BaseUrl.StartsWith("https://")) {
    $probe = Get-Status -Uri "$BaseUrl/health"
    if ($probe -lt 0) {
        Write-Bad "the https endpoint did not answer - unreachable, or the certificate did not validate"
    } else {
        Write-Ok "https answers and the certificate validates"

        # HSTS, read off a real response.
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 20
            $hsts = Get-Header -Response $resp -Name "Strict-Transport-Security"
            Check ([bool]$hsts) "Strict-Transport-Security is set ($hsts)"
        } catch {
            Write-Warn "could not read the response headers: $($_.Exception.Message)"
        }

        # http -> https. -MaximumRedirection 0 makes the redirect itself the result rather than following it.
        $httpUrl = "http://" + $BaseUrl.Substring("https://".Length) + "/health"
        $redirect = -1
        try {
            $r = Invoke-WebRequest -Uri $httpUrl -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 20
            $redirect = [int]$r.StatusCode
        } catch {
            if ($_.Exception.Response) { $redirect = [int]$_.Exception.Response.StatusCode }
        }
        if (@(301, 302, 307, 308) -contains $redirect) {
            Write-Ok "plain http redirects ($redirect) instead of serving the API"
        } elseif ($redirect -eq 200) {
            Write-Bad "plain http SERVES the API (200) - a client that forgets the scheme sends its password in the clear"
        } else {
            Write-Warn "plain http answered $redirect - not a redirect, but not the API either"
        }
    }
} elseif ($AllowHttp) {
    Write-Warn "-AllowHttp: TLS checks skipped. This is a rehearsal, not a preflight."
    Manual "TLS in front of the API - this run was against plain http."
} else {
    Write-Bad "the base URL is not https. Pass -AllowHttp only to rehearse the script locally."
}

# ---------------------------------------------------------------------------- 2. identity
Write-Step "2. Identity"
$health = $null
try {
    $health = Get-Json -Uri "$BaseUrl/health"
    Write-Info ($health | ConvertTo-Json -Compress)
} catch {
    Write-Bad "GET /health failed: $($_.Exception.Message)"
}

if ($health) {
    Check ($health.status -eq "ok") "status ok"

    $expected = $null
    $versionFile = Join-Path $PSScriptRoot "version.json"
    if (Test-Path $versionFile) {
        try { $expected = (Get-Content $versionFile -Raw | ConvertFrom-Json).version } catch { }
    }
    if ($expected) {
        Check ($health.version -eq $expected) `
            ("the deployed version is $($health.version) and tools/version.json says $expected")
    } else {
        Write-Warn "could not read tools/version.json - version not compared"
    }

    # role is 10.4: behind the public name this must not be the scheduler.
    if ($health.PSObject.Properties.Name -contains "role") {
        if ($health.role -eq "api") {
            Write-Ok "this instance is role 'api' - the scheduler is a separate process"
        } elseif ($health.role -eq "both") {
            Write-Warn "role 'both': the API and the Hangfire scheduler share this process. It works, but the 9.6 load test measured matchday resolution competing with player requests - split it with Jobs__Role."
        } else {
            Write-Bad "role '$($health.role)': the public name is pointed at a WORKER, which maps no player endpoints"
        }
    } else {
        Write-Warn "/health reports no role - this server predates the 10.4 process split"
    }
}

# ---------------------------------------------------------------------------- 3. readiness
Write-Step "3. Readiness"
$ready = Get-Status -Uri "$BaseUrl/health/ready"
Check ($ready -eq 200) "GET /health/ready -> $ready (PostgreSQL migrated + Redis answering)"

# ---------------------------------------------------------------------------- 4. dev doors
#
# THIS SECTION PROBES, IT DOES NOT INVOKE. The first version of this script asked each route with the
# verb it is actually mapped for, which in production is harmless (they are all 404) but on a NON-prod
# stack meant really resetting the world, really seeding a league and really advancing the calendar -
# the rehearsal run destroyed the very thing it was rehearsing against.
#
# So the mutating routes are asked with a verb they are NOT mapped for. ASP.NET routing answers 405 when
# the PATH matches but the method does not, and 404 when the path is not mapped at all - which is exactly
# the question being asked, and the endpoint body never runs. Only /hangfire keeps a GET: the dashboard
# is middleware rather than a mapped route, so it does not produce a 405, and rendering its index page
# changes nothing.
Write-Step "4. Dev endpoints are not there"
$devDoors = @(
    @{ Probe = "Delete"; Path = "/internal/dev/test-league";        Why = "UNAUTHENTICATED league seeding" },
    @{ Probe = "Delete"; Path = "/internal/dev/reset";              Why = "UNAUTHENTICATED world reset" },
    @{ Probe = "Delete"; Path = "/internal/dev/ranked/fill";        Why = "UNAUTHENTICATED ladder seeding" },
    @{ Probe = "Delete"; Path = "/internal/ranked/tick";            Why = "hand-advancing the ranked calendar" },
    @{ Probe = "Delete"; Path = "/internal/ranked/auctions/settle"; Why = "hand-settling auctions" },
    @{ Probe = "Delete"; Path = "/internal/sim/match";              Why = "the raw simulation endpoint" },
    @{ Probe = "Delete"; Path = "/internal/sim/determinism";        Why = "the determinism harness" },
    @{ Probe = "Delete"; Path = "/internal/jobs/heartbeat";         Why = "enqueueing jobs by hand" },
    @{ Probe = "Get";    Path = "/hangfire";                        Why = "the Hangfire dashboard" }
)
foreach ($door in $devDoors) {
    $status = Get-Status -Method $door.Probe -Uri ($BaseUrl + $door.Path)
    if ($status -eq 404) {
        Write-Ok "$($door.Path) -> 404 (not mapped)"
    } elseif ($status -eq 405) {
        Write-Bad "$($door.Path) -> 405 - the route EXISTS ($($door.Why) is exposed); the probe used a verb it does not accept, so nothing was executed"
    } elseif ($status -lt 0) {
        Write-Warn "$($door.Path) -> no answer (treated as unreachable, not as closed)"
    } else {
        Write-Bad "$($door.Path) -> $status - $($door.Why) is EXPOSED"
    }
}

# ---------------------------------------------------------------------------- 5. secrets
Write-Step "5. No shipped signing key is live"
$shippedKeys = @(
    @{ Name = "appsettings.json default"; Key = "fts_dev_signing_key_change_me_please_min_32_bytes" },
    @{ Name = "docker-compose.yml dev key"; Key = "fts_local_docker_signing_key_change_me_min_32_bytes" }
)
foreach ($candidate in $shippedKeys) {
    $token = New-ForgedToken $candidate.Key
    $status = Get-Status -Uri "$BaseUrl/auth/me" -Headers @{ Authorization = "Bearer $token" }
    if ($status -eq 401) {
        Write-Ok "a token signed with the $($candidate.Name) is REJECTED"
    } elseif ($status -lt 0) {
        Write-Warn "could not test the $($candidate.Name) - no answer"
    } else {
        Write-Bad "a token signed with the $($candidate.Name) was ACCEPTED ($status). Anyone with this repository can mint tokens. Set Jwt__SigningKey and restart before going any further."
    }
}
Manual "Integrity__SignalSalt overridden - it cannot be probed from outside; check the env file / container environment."

# ---------------------------------------------------------------------------- 6. admin
Write-Step "6. Live ops"
$anon = Get-Status -Uri "$BaseUrl/admin/metrics"
Check ($anon -eq 401) "/admin/metrics is 401 anonymously -> $anon"

if ($AdminEmail -and $AdminPassword) {
    try {
        $auth = Get-Json -Method Post -Uri "$BaseUrl/auth/login" -Body @{ email = $AdminEmail; password = $AdminPassword }
        $headers = @{ Authorization = "Bearer $($auth.accessToken)" }
        $m = Get-Json -Uri "$BaseUrl/admin/metrics" -Headers $headers

        Check ($m.environment -eq "Production") "the server reports environment '$($m.environment)'"
        Write-Info ("{0} accounts, {1} coaches, {2} worlds, balance revision {3}" -f `
            $m.accounts, $m.rankedCoaches, $m.rankedWorlds, $m.balanceRevision)

        if ($null -ne $m.jobServers -and $m.jobServers -ge 1) {
            if ($health -and $health.role -eq "api") {
                Write-Ok "$($m.jobServers) Hangfire server(s) registered while this instance is role 'api' - the worker is its own process"
            } else {
                Write-Ok "$($m.jobServers) Hangfire server(s) registered"
            }
        } else {
            Write-Bad "no Hangfire server is registered - nothing will resolve a matchday or settle an auction"
        }

        Check ($m.failedJobs -le 0) "no failed background jobs ($($m.failedJobs))"
        if ($m.worstFixtureLagSeconds -gt $MaxFixtureLagSeconds) {
            Write-Bad ("the ranked calendar is BEHIND: {0} overdue, worst {1}s past kickoff" -f `
                $m.overdueFixtures, $m.worstFixtureLagSeconds)
        } else {
            Write-Ok ("the ranked calendar is on time ({0} overdue, worst {1}s)" -f `
                $m.overdueFixtures, $m.worstFixtureLagSeconds)
        }
    } catch {
        Write-Bad "could not read /admin/metrics as $AdminEmail (is the account an admin?): $($_.Exception.Message)"
    }
} else {
    Write-Info "no -AdminEmail/-AdminPassword: skipping the checks that need an operator account"
    Manual "Read /admin/metrics as an admin and confirm environment=Production and jobServers>=1."
}

# ---------------------------------------------------------------------------- 7. CORS
Write-Step "7. Browser clients"
if ($WebOrigin) {
    try {
        $resp = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 20 `
            -Headers @{ Origin = $WebOrigin }
        $allow = Get-Header -Response $resp -Name "Access-Control-Allow-Origin"
        if ($allow -and ($allow -eq $WebOrigin -or $allow -eq "*")) {
            Write-Ok "the API allows $WebOrigin ($allow)"
        } else {
            Write-Bad "no Access-Control-Allow-Origin for $WebOrigin - the WebGL build and the public account-deletion page cannot call this API. Set Cors__AllowedOrigins__0."
        }
    } catch {
        Write-Bad "the CORS probe failed: $($_.Exception.Message)"
    }
} else {
    Write-Info "no -WebOrigin: CORS not checked"
    Manual "If a browser client is shipped (WebGL build, delete-account.html, admin dashboard), set Cors__AllowedOrigins__0 and re-run with -WebOrigin."
}

# ---------------------------------------------------------------------------- 8. local files
Write-Step "8. On this machine"
if ($EnvFile) {
    if (-not (Test-Path $EnvFile)) {
        Write-Bad "-EnvFile $EnvFile does not exist"
    } else {
        $lines = Get-Content $EnvFile
        $placeholders = @($lines | Where-Object { $_ -match "CHANGE_ME" })
        Check ($placeholders.Count -eq 0) "no CHANGE_ME placeholders left in $EnvFile ($($placeholders.Count) found)"

        foreach ($required in @("JWT_SIGNING_KEY", "INTEGRITY_SIGNAL_SALT", "POSTGRES_PASSWORD")) {
            $line = @($lines | Where-Object { $_ -match ("^\s*" + $required + "\s*=") })
            if ($line.Count -eq 0) {
                Write-Bad "$required is not set in $EnvFile"
            } else {
                $value = ($line[0] -split "=", 2)[1].Trim()
                Check ($value.Length -ge 24) "$required is set and long enough ($($value.Length) chars)"
            }
        }

        # Committed secrets are the failure mode that survives every rotation.
        try {
            $tracked = & git ls-files --error-unmatch $EnvFile 2>$null
            if ($LASTEXITCODE -eq 0 -and $tracked) {
                Write-Bad "$EnvFile is TRACKED BY GIT - the deployment's secrets are in the repository history"
            } else {
                Write-Ok "$EnvFile is not tracked by git"
            }
        } catch {
            Write-Info "git not available - could not check whether $EnvFile is tracked"
        }
    }
} else {
    Write-Info "no -EnvFile: the production environment file was not inspected"
    Manual "Check the production env file for leftover CHANGE_ME values and that it is gitignored."
}

if ($BackupPath) {
    if (-not (Test-Path $BackupPath)) {
        Write-Bad "-BackupPath $BackupPath does not exist - there is no backup"
    } else {
        $newest = Get-ChildItem -Path $BackupPath -File -Recurse |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $newest) {
            Write-Bad "$BackupPath is empty - there is no backup"
        } else {
            $ageHours = [math]::Round(((Get-Date) - $newest.LastWriteTime).TotalHours, 1)
            if ($ageHours -le $MaxBackupAgeHours) {
                Write-Ok ("newest backup {0} is {1}h old ({2:N2} MB)" -f $newest.Name, $ageHours, ($newest.Length / 1MB))
            } else {
                Write-Bad ("the newest backup {0} is {1}h old (limit {2}h)" -f $newest.Name, $ageHours, $MaxBackupAgeHours)
            }
        }
    }
    Manual "A backup that has never been READ BACK is a hope, not a backup - run tools\restore-db.ps1 into a side database and compare its row counts with /admin/metrics."
} else {
    Write-Info "no -BackupPath: backups not checked"
    Manual "Take a database backup with tools\backup-db.ps1 and test the restore before opening."
}

# ---------------------------------------------------------------------------- verdict
Write-Host ""
Write-Host "----------------------------------------------------------------" -ForegroundColor White
if ($script:Failures -eq 0) {
    Write-Host "PREFLIGHT PASSED" -ForegroundColor Green -NoNewline
    Write-Host ("  ({0} warning(s))" -f $script:Warnings)
} else {
    Write-Host ("PREFLIGHT FAILED - {0} blocker(s), {1} warning(s)" -f $script:Failures, $script:Warnings) -ForegroundColor Red
}

if ($script:Manual.Count -gt 0) {
    Write-Host ""
    Write-Host "Still on you (this script cannot settle these):" -ForegroundColor Yellow
    foreach ($item in $script:Manual) { Write-Host "  - $item" -ForegroundColor Yellow }
}
Write-Host ""
Write-Host "The per-store paperwork is sections 2-5 of docs/store/release-checklist.md." -ForegroundColor DarkGray

exit ([int]($script:Failures -gt 0))
