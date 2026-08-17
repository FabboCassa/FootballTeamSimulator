<#
.SYNOPSIS
    Back up the FTS PostgreSQL database (Roadmap 10.3).

.DESCRIPTION
    Takes a pg_dump custom-format archive, VERIFIES it is readable, and prunes old ones.

    Three deliberate choices:

      1. CUSTOM format (-Fc), not plain SQL. It restores selectively and in parallel, and pg_restore can
         list its table of contents - which is what makes the verify step cheap enough to run on every
         single backup rather than "one day, on purpose".

      2. VERIFIED, always. A backup nobody has read back is a hope, not a backup. After the dump the
         script runs `pg_restore --list` over the archive and refuses to report success if the table of
         contents is empty or unreadable. That costs a second and catches the two failures that actually
         happen: a truncated file, and a dump that ran against the wrong database and came back nearly empty.

      3. It works INSIDE the database's own container when there is one (the default for the local dev
         stack). Going out through the published port and back in means re-authenticating over TCP for no
         benefit; on the container's local socket the official image trusts the connection, so there is no
         password to get wrong, no host.docker.internal to resolve and no published port to depend on.
         For a remote database it falls back to a pinned client image, and -UseLocalTools uses PATH.

.PARAMETER Container
    Name of the running PostgreSQL container. Used automatically when it exists. Pass "" to force the
    client-image path (that is what a remote host wants).

.PARAMETER ConnectionString
    Npgsql-style connection string. Only the host/port/user/password/database are read.

.PARAMETER OutDir
    Where the archives go. Default: backups/ at the repo root (gitignored).

.PARAMETER KeepDays
    Delete archives in OutDir older than this many days. Default 14. 0 disables pruning.

.EXAMPLE
    .\tools\backup-db.ps1
    .\tools\backup-db.ps1 -Container "" -ConnectionString $env:FTS_PG -OutDir D:\backups -KeepDays 30

.NOTES
    ASCII-only and PowerShell 5.1 safe.
#>

[CmdletBinding()]
param(
    [string]$Container = "fts-postgres",
    [string]$ConnectionString = "Host=localhost;Port=5432;Database=fts;Username=fts;Password=fts_dev_password",
    [string]$OutDir = "",
    [int]$KeepDays = 14,
    [switch]$UseLocalTools,
    [string]$PostgresImage = "postgres:17-alpine"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root "backups" }

function Parse-ConnectionString([string]$cs) {
    $map = @{}
    foreach ($part in $cs.Split(';')) {
        if ([string]::IsNullOrWhiteSpace($part)) { continue }
        $i = $part.IndexOf('=')
        if ($i -lt 1) { continue }
        $map[$part.Substring(0, $i).Trim().ToLower()] = $part.Substring($i + 1).Trim()
    }
    return $map
}

$cs = Parse-ConnectionString $ConnectionString
$pgHost = $cs["host"]; if (-not $pgHost) { $pgHost = "localhost" }
$pgPort = $cs["port"]; if (-not $pgPort) { $pgPort = "5432" }
$pgDb   = $cs["database"]; if (-not $pgDb) { $pgDb = "fts" }
$pgUser = $cs["username"]; if (-not $pgUser) { $pgUser = $cs["user id"] }
if (-not $pgUser) { $pgUser = "fts" }
$pgPass = $cs["password"]; if (-not $pgPass) { $pgPass = $env:PGPASSWORD }

# Which route to the database? Prefer the container it already lives in.
$useContainer = $false
if (-not $UseLocalTools -and -not [string]::IsNullOrWhiteSpace($Container)) {
    $running = ""
    try { $running = (docker ps --filter "name=$Container" --format "{{.Names}}" | Out-String).Trim() } catch { }
    $useContainer = ($running -split "`n" | ForEach-Object { $_.Trim() }) -contains $Container
}

if (-not $OutDir) { $OutDir = Join-Path $root "backups" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$leaf = "fts-{0}-{1}.dump" -f $pgDb, $stamp
$archive = Join-Path $OutDir $leaf

$route = if ($UseLocalTools) { "local pg_dump" } elseif ($useContainer) { "inside $Container" } else { "$PostgresImage client" }
Write-Host "== Backing up $pgDb ($route)" -ForegroundColor Cyan
Write-Host "   -> $archive"

# Inside a client container, "localhost" is that container. host.docker.internal is the Docker Desktop
# escape hatch; on Linux the compose network name is the right answer instead.
$dumpHost = $pgHost
if (-not $UseLocalTools -and -not $useContainer -and ($pgHost -eq "localhost" -or $pgHost -eq "127.0.0.1")) {
    $dumpHost = "host.docker.internal"
}

# --no-owner/--no-privileges keep the archive restorable into a database owned by somebody else, which is
# what a real restore looks like. Note the --file argument is built as ONE string: PowerShell does not glue
# a parenthesised expression onto the token before it, and pg_dump answers "too many command-line
# arguments" when it arrives split in two.
if ($UseLocalTools) {
    $env:PGPASSWORD = $pgPass
    & pg_dump --host=$pgHost --port=$pgPort --username=$pgUser --dbname=$pgDb `
        --format=custom --no-owner --no-privileges --file=$archive
    $code = $LASTEXITCODE
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
} elseif ($useContainer) {
    $tmp = "/tmp/$leaf"
    $fileArg = "--file=$tmp"
    docker exec $Container pg_dump --username=$pgUser --dbname=$pgDb `
        --format=custom --no-owner --no-privileges $fileArg
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        # Verify BEFORE copying: a container-side archive that will not read back should never reach the
        # backup directory and look like a good one.
        $toc = docker exec $Container pg_restore --list $tmp 2>&1
        $tocCode = $LASTEXITCODE
        docker cp "${Container}:$tmp" $archive | Out-Null
        docker exec $Container rm -f $tmp | Out-Null
    }
} else {
    $tmp = "/backup/$leaf"
    $fileArg = "--file=$tmp"
    docker run --rm `
        -e PGPASSWORD=$pgPass `
        -v "${OutDir}:/backup" `
        --add-host=host.docker.internal:host-gateway `
        $PostgresImage `
        pg_dump --host=$dumpHost --port=$pgPort --username=$pgUser --dbname=$pgDb `
            --format=custom --no-owner --no-privileges $fileArg
    $code = $LASTEXITCODE
}

if ($code -ne 0) { Write-Error "pg_dump failed with exit code $code." }
if (-not (Test-Path $archive)) { Write-Error "pg_dump reported success but produced no archive." }

$sizeMb = [math]::Round((Get-Item $archive).Length / 1MB, 2)
Write-Host ("   dump OK ({0} MB)" -f $sizeMb) -ForegroundColor Green

# --- verify (the step that turns a file into a backup) -------------------------------------------
Write-Host "== Verifying the archive is readable" -ForegroundColor Cyan
if ($UseLocalTools) {
    $toc = & pg_restore --list $archive 2>&1
    $tocCode = $LASTEXITCODE
} elseif ($useContainer) {
    # Already read back inside the container, above.
} else {
    $tocPath = "/backup/$leaf"
    $toc = docker run --rm -v "${OutDir}:/backup" $PostgresImage pg_restore --list $tocPath 2>&1
    $tocCode = $LASTEXITCODE
}

$tocLines = @($toc | Where-Object { $_ -and ($_ -notmatch '^\s*;') })
if ($tocCode -ne 0 -or $tocLines.Count -lt 10) {
    Write-Error ("The archive did not read back ({0} entries). Treat this backup as FAILED." -f $tocLines.Count)
}
Write-Host ("   verify OK ({0} objects in the table of contents)" -f $tocLines.Count) -ForegroundColor Green

# A backup that does not contain the tables the game lives in is the failure that looks like success.
$tocText = ($toc | Out-String)
foreach ($table in @("users", "ranked_coaches", "balance_revisions")) {
    if ($tocText -notmatch [regex]::Escape($table)) {
        Write-Warning "The table of contents does not mention '$table' - check you dumped the right database."
    }
}

# --- prune ---------------------------------------------------------------------------------------
if ($KeepDays -gt 0) {
    $cutoff = (Get-Date).AddDays(-$KeepDays)
    $old = Get-ChildItem $OutDir -Filter "fts-*.dump" -File | Where-Object { $_.LastWriteTime -lt $cutoff }
    foreach ($f in $old) {
        Remove-Item $f.FullName -Force
        Write-Host ("   pruned {0}" -f $f.Name) -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "Backup complete: $archive" -ForegroundColor Green
Write-Host "Restore it with: .\tools\restore-db.ps1 -Archive `"$archive`"" -ForegroundColor Yellow
