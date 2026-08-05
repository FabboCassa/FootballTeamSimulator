<#
.SYNOPSIS
    Live smoke test for account deletion (Roadmap 10.2a) against a running server.

.DESCRIPTION
    Deletion is the one irreversible call in the API and a store-blocking requirement, so it is
    worth watching it happen on a real (docker compose) server rather than only in the tests.

    The script builds an account that has something in every corner of the system, deletes it, and
    then checks BOTH halves of the promise: what must be gone is gone, and what must survive
    survived.

      1. registers a fresh coach and registers a push device
      2. creates a private league (a whole generated world hangs off it)
      3. joins the ranked ladder (claims a seat, which the deletion must free)
      4. refuses a deletion with the WRONG password, and proves the account still works
      5. refuses a deletion with NO token
      6. deletes for real, and prints the returned summary
      7. checks the credentials are dead: the old access token, the password, the refresh token
      8. checks a SECOND deletion is harmless (401, nothing to delete)
      9. with -Friend, repeats the league half with two accounts: the league SURVIVES the
         deletion of its creator and ownership passes to the member who stayed

    Nothing here needs a dev gate: it is all public API. It does need the server to allow
    registration (it always does) and, for step 3, the ranked ladder to be enabled.

.PARAMETER BaseUrl
    Server root. Default http://localhost:8080

.PARAMETER SkipRanked
    Skip the ladder enrolment (step 3). Enrolling materialises a ranked world the first time a
    placement group fills, which on a cold database is the slowest part of the run.

.PARAMETER Friend
    Also run the two-account league check (step 9).

.EXAMPLE
    .\tools\smoke-account-deletion.ps1
    .\tools\smoke-account-deletion.ps1 -Friend
    .\tools\smoke-account-deletion.ps1 -BaseUrl http://localhost:8080 -SkipRanked
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8080",
    [switch]$SkipRanked,
    [switch]$Friend
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

$script:Failures = 0
$Password = "Password1"

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

# A non-2xx here means the run is broken, so it throws.
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

# Same call, but a non-2xx is an EXPECTED outcome: returns the status code instead of throwing.
function Invoke-ApiStatus {
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
            $null = Invoke-RestMethod -Method $Method -Uri $uri -Headers $Headers -Body $json -ContentType "application/json"
        }
        else {
            $null = Invoke-RestMethod -Method $Method -Uri $uri -Headers $Headers
        }
        return 200
    }
    catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return -1
    }
}

function New-Coach([string]$Label) {
    $email = "del_" + [guid]::NewGuid().ToString("N").Substring(0, 10) + "@example.com"
    $auth = Invoke-Api -Method Post -Path "/auth/register" -Body @{
        email       = $email
        password    = $Password
        displayName = $Label
    }
    return [pscustomobject]@{
        Email   = $email
        Token   = $auth.accessToken
        Refresh = $auth.refreshToken
        UserId  = $auth.profile.userId
        Headers = @{ Authorization = "Bearer $($auth.accessToken)" }
    }
}

Write-Host "Account deletion smoke (Roadmap 10.2a)" -ForegroundColor White
Write-Host "Server: $BaseUrl"

# ---------------------------------------------------------------- 0. health
Write-Step "Health"
$health = Invoke-Api -Method Get -Path "/health"
Write-Host "   $($health | ConvertTo-Json -Compress)"

# ---------------------------------------------------------------- 1. an account with a life
Write-Step "Build an account that has something everywhere"
$me = New-Coach "Delete Mister"
Check ([string]::IsNullOrEmpty($me.Token) -eq $false) "registered $($me.Email)"

$device = "smokedev_" + [guid]::NewGuid().ToString("N").Substring(0, 12)
$null = Invoke-Api -Method Post -Path "/notifications/devices" -Headers $me.Headers -Body @{
    token    = $device
    platform = 0            # DevicePlatform.Android
}
Write-Ok "registered a push device"

$league = Invoke-Api -Method Post -Path "/leagues" -Headers $me.Headers -Body @{
    name = "Smoke FC"
    size = 4
    mode = 0                # LeagueMode.AllReady
}
$leagueId = $league.league.id
Write-Ok "created private league '$($league.league.name)' (invite $($league.league.inviteCode)) with a generated world"

$enrolled = $false
if (-not $SkipRanked) {
    $state = Invoke-Api -Method Post -Path "/ranked/enrol" -Headers $me.Headers
    $enrolled = ($state.enrolled -eq $true)
    Check $enrolled "joined the ladder (world '$($state.worldName)', group '$($state.groupName)') - that seat must come back as AI"
}
else {
    Write-Host "   (ranked enrolment skipped)"
}

# ---------------------------------------------------------------- 2. the guards
Write-Step "The guards"
$wrong = Invoke-ApiStatus -Method Post -Path "/auth/account/delete" -Headers $me.Headers -Body @{ password = "NotMyPassword1" }
Check ($wrong -eq 401) "wrong password is refused (401, got $wrong)"

$stillMe = Invoke-Api -Method Get -Path "/auth/me" -Headers $me.Headers
Check ($stillMe.email -eq $me.Email) "the account is untouched after the refused attempt"

$noToken = Invoke-ApiStatus -Method Post -Path "/auth/account/delete" -Body @{ password = $Password }
Check ($noToken -eq 401) "no token is refused (401, got $noToken)"

# ---------------------------------------------------------------- 3. the deletion
Write-Step "Delete the account for good"
$summary = Invoke-Api -Method Post -Path "/auth/account/delete" -Headers $me.Headers -Body @{ password = $Password }
Write-Host "   summary: $($summary | ConvertTo-Json -Compress)"

Check ($summary.privateLeaguesLeft -ge 1) "left $($summary.privateLeaguesLeft) private league(s)"
Check ($summary.devicesRemoved -ge 1) "removed $($summary.devicesRemoved) device registration(s)"
Check ($summary.sessionsRevoked -ge 1) "revoked $($summary.sessionsRevoked) session(s)"
if ($enrolled) {
    Check ($summary.rankedHistoryAnonymised -eq $true) "the ladder record was anonymised, not deleted (seat freed)"
}

# ---------------------------------------------------------------- 4. the credentials are dead
Write-Step "Nothing can get back in"
$meAfter = Invoke-ApiStatus -Method Get -Path "/auth/me" -Headers $me.Headers
Check ($meAfter -eq 401) "the old access token no longer resolves to an account (401, got $meAfter)"

$login = Invoke-ApiStatus -Method Post -Path "/auth/login" -Body @{ email = $me.Email; password = $Password }
Check ($login -eq 401) "the email + password no longer log in (401, got $login)"

$refresh = Invoke-ApiStatus -Method Post -Path "/auth/refresh" -Body @{ refreshToken = $me.Refresh }
Check ($refresh -eq 401) "the refresh token is gone (401, got $refresh)"

# NOTE: the JWT is stateless, so this request is still ALLOWED through - it just finds nothing.
# 404 is the right answer here: the league had one member, so leaving it tore it (and its world) down.
$leagueGone = Invoke-ApiStatus -Method Get -Path "/leagues/$leagueId" -Headers $me.Headers
Check ($leagueGone -eq 404) "the one-member league was torn down with the account (404, got $leagueGone)"

# Re-registering the same address must be possible: deletion means gone, not banned.
$reuse = Invoke-ApiStatus -Method Post -Path "/auth/register" -Body @{
    email       = $me.Email
    password    = $Password
    displayName = "Second Life"
}
Check ($reuse -eq 200) "the same email can register again, as a brand-new account (got $reuse)"

# ---------------------------------------------------------------- 5. repeating it is harmless
Write-Step "Deleting twice"
$again = Invoke-ApiStatus -Method Post -Path "/auth/account/delete" -Headers $me.Headers -Body @{ password = $Password }
Check ($again -eq 401) "a second deletion with the old token is a harmless 401 (got $again)"

# ---------------------------------------------------------------- 6. the league survives its creator
if ($Friend) {
    Write-Step "A league with another member SURVIVES"
    # NOTE: not $friend - PowerShell variable names are case-insensitive, so it would overwrite the
    # -Friend switch parameter itself (and fail casting a PSCustomObject to a SwitchParameter).
    $creator = New-Coach "Creator Mister"
    $mate = New-Coach "Friend Mister"

    $shared = Invoke-Api -Method Post -Path "/leagues" -Headers $creator.Headers -Body @{
        name = "Survivors FC"
        size = 4
        mode = 0
    }
    $sharedId = $shared.league.id
    $null = Invoke-Api -Method Post -Path "/leagues/join" -Headers $mate.Headers -Body @{
        inviteCode = $shared.league.inviteCode
    }
    Write-Ok "two accounts in league '$($shared.league.name)'"

    $null = Invoke-Api -Method Post -Path "/auth/account/delete" -Headers $creator.Headers -Body @{ password = $Password }
    Write-Ok "the creator deleted their account"

    $after = Invoke-Api -Method Get -Path "/leagues/$sharedId" -Headers $mate.Headers
    Check ($after.members.Count -eq 1) "the league is still there with $($after.members.Count) member(s)"
    Check ($after.members[0].userId -eq $mate.UserId) "the member who stayed is still in it"
    Check ($after.league.isCreator -eq $true) "ownership passed to the member who stayed"
    Check ($after.clubs.Count -gt 0) "the generated world survived ($($after.clubs.Count) clubs)"

    # Tidy up after ourselves so a re-run does not leave leagues behind.
    $null = Invoke-Api -Method Post -Path "/auth/account/delete" -Headers $mate.Headers -Body @{ password = $Password }
    Write-Ok "cleaned up: the last member left, so the league was torn down too"
}
else {
    Write-Host ""
    Write-Host "(skipped the two-account league check - re-run with -Friend to include it)" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- summary
Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "ALL CHECKS PASSED - deletion removes the person and leaves the game consistent." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures) CHECK(S) FAILED - see the FAIL lines above." -ForegroundColor Red
exit 1
