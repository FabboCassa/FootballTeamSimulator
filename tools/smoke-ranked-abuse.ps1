<#
.SYNOPSIS
    Scripted abuse smoke for the ranked ladder (Phase 9.5) against a running server.

.DESCRIPTION
    Runs the abuse scenarios the 9.5 roadmap check calls for and reports PASS/FAIL per check:

      1. MULTI-ACCOUNT   two accounts reporting the same device id both join the ladder,
                         but are seated in DIFFERENT groups, and the link is flagged
      2. COLLUSION       a star gifted for pocket change is refused (409 integrity_blocked);
                         a wild overpay is refused; an honest transfer still completes
      3. BID SPAM        hammering an auction lot trips the per-account rate limit (429)
      4. DEADLINE        a lineup submitted while the next kickoff is still ahead is accepted
                         (the refusal-after-kickoff path is covered by Api.Tests, which can
                         compress the calendar; a live server's matchday is a day away)
      5. REPORTS         a coach can report a rival; reporting yourself is refused
      6. QUEUE           the review queue lists everything the run produced

    Needs the dev gates ON in the running server (they are in docker-compose):
      Dev:ExposeSeedEndpoints       = true  -> /internal/dev/ranked/fill
      Ranked:ExposeInternalEndpoints= true  -> /internal/ranked/tick, /internal/ranked/integrity/flags

    Re-runnable: every run registers fresh accounts.

.PARAMETER BaseUrl
    Server root. Default http://localhost:8080

.PARAMETER BidAttempts
    How many bids to fire at one lot in the spam scenario. Default 40 (the shipped limit is
    10 per 10 seconds per account).

.EXAMPLE
    .\tools\smoke-ranked-abuse.ps1
    .\tools\smoke-ranked-abuse.ps1 -BaseUrl http://localhost:8080 -BidAttempts 60
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8080",
    [int]$BidAttempts = 40
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
            $json = $Body | ConvertTo-Json -Depth 12 -Compress
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
            $json = $Body | ConvertTo-Json -Depth 12 -Compress
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

function New-Coach {
    param([string]$DeviceId)
    $email = "abuse_" + [guid]::NewGuid().ToString("N").Substring(0, 10) + "@example.com"
    $auth = Invoke-Api -Method Post -Path "/auth/register" -Body @{
        email       = $email
        password    = "Password1"
        displayName = "Abuse Mister"
    }
    $headers = @{ Authorization = "Bearer $($auth.accessToken)" }
    if (-not [string]::IsNullOrEmpty($DeviceId)) { $headers["X-Fts-Device"] = $DeviceId }

    # The real client reads the ranked home before joining - that GET is what records the fingerprint.
    $null = Invoke-Api -Method Get -Path "/ranked/me" -Headers $headers
    $state = Invoke-Api -Method Post -Path "/ranked/enrol" -Headers $headers

    return [pscustomobject]@{
        Email   = $email
        Headers = $headers
        State   = $state
    }
}

Write-Host "Ranked abuse & integrity smoke (Phase 9.5)" -ForegroundColor White
Write-Host "Server: $BaseUrl"

# ---------------------------------------------------------------- 0. health
Write-Step "Health"
$health = Invoke-Api -Method Get -Path "/health"
Write-Host "   $($health | ConvertTo-Json -Compress)"

# ---------------------------------------------------------------- 1. multi-account
Write-Step "Scenario 1: two accounts on the same device"
$device = "smoke-device-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
$twinA = New-Coach -DeviceId $device
$twinB = New-Coach -DeviceId $device

Write-Host "   A -> group $($twinA.State.groupName) [$($twinA.State.groupId)]"
Write-Host "   B -> group $($twinB.State.groupName) [$($twinB.State.groupId)]"
Check ($twinA.State.enrolled -eq $true -and $twinB.State.enrolled -eq $true) "both accounts joined the ladder (nobody is banned)"
Check ($twinA.State.groupId -ne $twinB.State.groupId) "they were seated in DIFFERENT groups"

# ---------------------------------------------------------------- 2. a tradeable pair
# NOTE: the counterparty is a dev BOT in the caller's own group, not a second human account. On a single
# machine every account shares one address and is created seconds after the last, which is exactly the
# pattern the multi-account heuristics separate - so two humans registered here would NEVER share a group
# (scenario 1 above is that behaviour on purpose). The bots enrol server-side with no fingerprint, so they
# fill the human's group normally, which is also how a solo tester plays this feature.
Write-Step "Scenario 2: trade against a bot coach in your own group"
$buyer = New-Coach -DeviceId $null

$fill = Invoke-Api -Method Post -Path "/internal/dev/ranked/fill"
Write-Host "   fill: $($fill | ConvertTo-Json -Compress)"
$tick = Invoke-Api -Method Post -Path "/internal/ranked/tick"
Write-Host "   tick: $($tick | ConvertTo-Json -Compress)"

$me = Invoke-Api -Method Get -Path "/ranked/me" -Headers $buyer.Headers
$groupId = $me.groupId
$group = Invoke-Api -Method Get -Path "/ranked/groups/$groupId" -Headers $buyer.Headers
$rivalSeat = $group.seats | Where-Object { $null -ne $_.userId -and $_.isYou -ne $true } | Select-Object -First 1
if ($null -eq $rivalSeat) {
    Write-Bad "no occupied rival seat in the group - cannot exercise the transfer guards"
}
else {
    Write-Host "   rival: $($rivalSeat.clubName) [club $($rivalSeat.clubExternalId)]"

    $offers = Invoke-Api -Method Get -Path "/ranked/offers" -Headers $buyer.Headers
    $budget = [long]$offers.yourBudget
    Check ($offers.marketOpen -eq $true) "a market window is open (budget $budget)"

    $rivalSquad = Invoke-Api -Method Get -Path "/ranked/clubs/$($rivalSeat.clubExternalId)/squad" -Headers $buyer.Headers
    $star = $rivalSquad.players | Sort-Object -Property marketValue -Descending | Select-Object -First 1
    # Priced high enough to be policed (250k floor), cheap enough that 3x still fits the budget.
    $overTarget = $rivalSquad.players |
        Where-Object { $_.marketValue -ge 250000 -and ($_.marketValue * 3) -le $budget } |
        Sort-Object -Property marketValue -Descending | Select-Object -First 1
    # Affordable at his full market value - the honest deal.
    $fairTarget = $rivalSquad.players |
        Where-Object { $_.marketValue -ge 250000 -and $_.marketValue -le $budget } |
        Sort-Object -Property marketValue | Select-Object -First 1
    Write-Host "   star: $($star.name) worth $($star.marketValue)"

    Write-Step "Scenario 2a: gift a star for pocket change"
    $giftFee = [long][math]::Max(25000, [math]::Floor($star.marketValue / 100))
    $giftStatus = Invoke-ApiStatus -Method Post -Path "/ranked/offers" -Headers $buyer.Headers -Body @{
        playerExternalId = $star.externalId
        fee              = $giftFee
    }
    Check ($giftStatus -eq 409) "a fee of $giftFee for a $($star.marketValue) player is REFUSED (409, got $giftStatus)"

    Write-Step "Scenario 2b: shovel the budget across with a wild overpay"
    if ($null -eq $overTarget) {
        Write-Host "   SKIP no player cheap enough to overpay 3x within a $budget budget (2a already proves the band)"
    }
    else {
        $overFee = [long]($overTarget.marketValue * 3)
        $overStatus = Invoke-ApiStatus -Method Post -Path "/ranked/offers" -Headers $buyer.Headers -Body @{
            playerExternalId = $overTarget.externalId
            fee              = $overFee
        }
        Check ($overStatus -eq 409) "a fee of $overFee for a $($overTarget.marketValue) player is REFUSED (409, got $overStatus)"
    }

    Write-Step "Scenario 2c: an honest transfer still works"
    if ($null -eq $fairTarget) {
        Write-Bad "no player in the rival squad is affordable at market value - the ranked economy needs a look"
    }
    else {
        $fairFee = [long]$fairTarget.marketValue
        $fairStatus = Invoke-ApiStatus -Method Post -Path "/ranked/offers" -Headers $buyer.Headers -Body @{
            playerExternalId = $fairTarget.externalId
            fee              = $fairFee
        }
        Check ($fairStatus -eq 200) "an offer of $fairFee at market value is accepted by the guard (got $fairStatus)"

        if ($fairStatus -eq 200) {
            # The bot coach answers his pending offers (the 9.2b dev autopilot).
            $bot = Invoke-Api -Method Post -Path "/internal/dev/ranked/$groupId/market/bot?rounds=1&accept=true"
            Write-Host "   bot: $($bot | ConvertTo-Json -Compress)"
            $squadAfter = Invoke-Api -Method Get -Path "/ranked/clubs/$($rivalSeat.clubExternalId)/squad" -Headers $buyer.Headers
            $stillThere = $squadAfter.players | Where-Object { $_.externalId -eq $fairTarget.externalId }
            Check ($null -eq $stillThere) "the bot accepted and the player left his squad"
        }
    }
}

# ---------------------------------------------------------------- 3. bid spam
Write-Step "Scenario 3: bid spam against one auction lot"
$auctions = Invoke-Api -Method Get -Path "/ranked/auctions" -Headers $buyer.Headers
if ($auctions.lots.Count -eq 0) {
    Write-Bad "no auction lots open - cannot exercise the bid limiter"
}
else {
    $lot = $auctions.lots | Sort-Object -Property startPrice | Select-Object -First 1
    $limited = 0
    $served = 0
    for ($i = 0; $i -lt $BidAttempts; $i++) {
        $code = Invoke-ApiStatus -Method Post -Path "/ranked/auctions/$($lot.id)/bid" -Headers $buyer.Headers -Body @{
            amount = [long]$lot.minNextBid
        }
        if ($code -eq 429) { $limited++ } else { $served++ }
    }
    Write-Host "   $BidAttempts bids -> $served reached the service, $limited refused by the limiter"
    Check ($limited -gt 0) "the rate limiter refused the spam (429)"
}

# ---------------------------------------------------------------- 4. input deadline
Write-Step "Scenario 4: lineup deadline"
$storedLineup = Invoke-Api -Method Get -Path "/ranked/lineup" -Headers $buyer.Headers
if ($null -eq $storedLineup.Slots -and $null -eq $storedLineup.slots) {
    Write-Bad "no stored lineup to resubmit (the season start should seed one)"
}
else {
    # Round-trip the seeded plan. Sim.Core writes the roles as NAMES; the Api binds enums as numbers, so
    # the roles are mapped back to their numeric values before resubmitting.
    $roles = @("Goalkeeper", "CentreBack", "FullBack", "DefensiveMidfielder", "CentralMidfielder",
        "AttackingMidfielder", "Winger", "Striker")
    $slots = @()
    foreach ($s in $storedLineup.Slots) {
        $roleValue = $s.Role
        if ($roleValue -is [string]) { $roleValue = [array]::IndexOf($roles, $s.Role) }
        $slots += @{ role = $roleValue; playerId = $s.PlayerId }
    }
    $submitStatus = Invoke-ApiStatus -Method Post -Path "/ranked/lineup" -Headers $buyer.Headers -Body @{
        lineup = @{ clubId = $storedLineup.ClubId; slots = $slots }
        tactic = $null
        plan   = $null
    }
    Check ($submitStatus -eq 200) "a lineup submitted before the next kickoff is accepted (got $submitStatus)"
    Write-Host "   (the after-kickoff refusal is covered by Api.Tests, which compresses the calendar)"
}

# ---------------------------------------------------------------- 5. reports
Write-Step "Scenario 5: player reports"
if ($null -ne $rivalSeat) {
    $reportStatus = Invoke-ApiStatus -Method Post -Path "/ranked/report" -Headers $buyer.Headers -Body @{
        subjectClubExternalId = $rivalSeat.clubExternalId
        reason                = 0
        details               = "smoke test report"
    }
    Check ($reportStatus -eq 200) "a report against a group rival is filed (got $reportStatus)"
}

$selfStatus = Invoke-ApiStatus -Method Post -Path "/ranked/report" -Headers $buyer.Headers -Body @{
    subjectClubExternalId = $me.clubExternalId
    reason                = 0
    details               = "myself"
}
Check ($selfStatus -eq 400) "reporting yourself is refused (got $selfStatus)"

# ---------------------------------------------------------------- 6. the review queue
Write-Step "Review queue (GET /internal/ranked/integrity/flags)"
$flags = Invoke-Api -Method Get -Path "/internal/ranked/integrity/flags?take=200"
$byKind = @{}
foreach ($f in $flags.flags) {
    $k = [string]$f.kind
    if ($byKind.ContainsKey($k)) { $byKind[$k] = $byKind[$k] + 1 } else { $byKind[$k] = 1 }
}
Write-Host "   $($flags.total) flag(s) total"
foreach ($k in $byKind.Keys) { Write-Host "   kind $k : $($byKind[$k])" }

# Kinds: 0 BlockedTransfer, 1 SuspiciousTransfer, 2 RepeatedTradingPair, 3 LinkedAccounts, 4 PlayerReport
Check ($byKind.ContainsKey("0")) "blocked transfers are on the queue"
Check ($byKind.ContainsKey("3")) "the linked-account pair is on the queue"
Check ($byKind.ContainsKey("4")) "the player report is on the queue"

# ---------------------------------------------------------------- summary
Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "ALL CHECKS PASSED - scripted abuse is blocked or flagged." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures) CHECK(S) FAILED - see the FAIL lines above." -ForegroundColor Red
exit 1
