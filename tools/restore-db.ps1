<#
.SYNOPSIS
    Restore an FTS PostgreSQL backup, and prove it worked (Roadmap 10.3).

.DESCRIPTION
    Restores a pg_dump custom-format archive produced by tools/backup-db.ps1 into a target database, then
    counts rows in the tables that matter so the result is checked rather than assumed.

    The default target is a SIDE database (fts_restore_test), not the live one. That is the whole point:
    the only way to know a backup restores is to restore it regularly, and the only way anyone actually
    does that is if it is safe and boring. Overwriting the live database requires naming it explicitly AND
    passing -Force, which then says what it is about to destroy and waits.

    Like the backup script, it works INSIDE the database's own container when there is one - the archive is
    copied in with `docker cp` and restored over the local socket, so there is no password to get wrong and
    no published port to depend on. For a remote database it uses a pinned client image instead.

.PARAMETER Archive
    Path to the .dump file.

.PARAMETER TargetDatabase
    Database to restore INTO. Created if missing. Default fts_restore_test.

.PARAMETER Force
    Required to restore into a database that already contains data.

.EXAMPLE
    .\tools\restore-db.ps1 -Archive .\backups\fts-fts-20260817-0300.dump
    .\tools\restore-db.ps1 -Archive .\backups\latest.dump -TargetDatabase fts -Force

.NOTES
    ASCII-only and PowerShell 5.1 safe.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [string]$Container = "fts-postgres",
    [string]$ConnectionString = "Host=localhost;Port=5432;Database=fts;Username=fts;Password=fts_dev_password",
    [string]$TargetDatabase = "fts_restore_test",
    [switch]$Force,
    [switch]$UseLocalTools,
    [string]$PostgresImage = "postgres:17-alpine"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Archive)) { Write-Error "No such archive: $Archive" }
$Archive = (Resolve-Path $Archive).Path
$archiveDir = Split-Path $Archive -Parent
$archiveName = Split-Path $Archive -Leaf

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
$pgUser = $cs["username"]; if (-not $pgUser) { $pgUser = $cs["user id"] }
if (-not $pgUser) { $pgUser = "fts" }
$pgPass = $cs["password"]; if (-not $pgPass) { $pgPass = $env:PGPASSWORD }

$useContainer = $false
if (-not $UseLocalTools -and -not [string]::IsNullOrWhiteSpace($Container)) {
    $running = ""
    try { $running = (docker ps --filter "name=$Container" --format "{{.Names}}" | Out-String).Trim() } catch { }
    $useContainer = ($running -split "`n" | ForEach-Object { $_.Trim() }) -contains $Container
}

$dumpHost = $pgHost
if (-not $UseLocalTools -and -not $useContainer -and ($pgHost -eq "localhost" -or $pgHost -eq "127.0.0.1")) {
    $dumpHost = "host.docker.internal"
}

function Invoke-Psql([string]$database, [string]$sql) {
    if ($UseLocalTools) {
        $env:PGPASSWORD = $pgPass
        $out = & psql --host=$pgHost --port=$pgPort --username=$pgUser --dbname=$database `
            --no-align --tuples-only --command=$sql 2>&1
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    } elseif ($useContainer) {
        $out = docker exec $Container psql --username=$pgUser --dbname=$database `
            --no-align --tuples-only --command=$sql 2>&1
    } else {
        $out = docker run --rm -e PGPASSWORD=$pgPass --add-host=host.docker.internal:host-gateway `
            $PostgresImage psql --host=$dumpHost --port=$pgPort --username=$pgUser --dbname=$database `
            --no-align --tuples-only --command=$sql 2>&1
    }
    return ($out | Out-String).Trim()
}

$route = if ($UseLocalTools) { "local pg_restore" } elseif ($useContainer) { "inside $Container" } else { "$PostgresImage client" }
Write-Host "== Restoring $archiveName into $TargetDatabase ($route)" -ForegroundColor Cyan

# Does the target exist, and does it already hold anything?
$exists = Invoke-Psql "postgres" ("SELECT 1 FROM pg_database WHERE datname = '{0}'" -f $TargetDatabase)
if ($exists -ne "1") {
    Write-Host "   creating database $TargetDatabase"
    [void](Invoke-Psql "postgres" ("CREATE DATABASE {0}" -f $TargetDatabase))
} else {
    $tables = Invoke-Psql $TargetDatabase "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'"
    if ([int]$tables -gt 0 -and -not $Force) {
        Write-Error ("$TargetDatabase already has $tables tables. Restoring would overwrite them - " +
                     "pass -Force if that is really what you want.")
    }
    if ([int]$tables -gt 0) {
        Write-Warning "Overwriting $tables existing tables in $TargetDatabase (-Force was given)."
        Start-Sleep -Seconds 3
    }
}

# --clean --if-exists so a re-restore into the same target is repeatable; --no-owner because the archive
# was taken without ownership and the restoring role is usually not the one that owned production.
if ($UseLocalTools) {
    $env:PGPASSWORD = $pgPass
    & pg_restore --host=$pgHost --port=$pgPort --username=$pgUser --dbname=$TargetDatabase `
        --clean --if-exists --no-owner --no-privileges --exit-on-error $Archive
    $code = $LASTEXITCODE
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
} elseif ($useContainer) {
    $tmp = "/tmp/$archiveName"
    docker cp $Archive "${Container}:$tmp" | Out-Null
    docker exec $Container pg_restore --username=$pgUser --dbname=$TargetDatabase `
        --clean --if-exists --no-owner --no-privileges --exit-on-error $tmp
    $code = $LASTEXITCODE
    docker exec $Container rm -f $tmp | Out-Null
} else {
    $tmp = "/backup/$archiveName"
    docker run --rm -e PGPASSWORD=$pgPass -v "${archiveDir}:/backup" `
        --add-host=host.docker.internal:host-gateway $PostgresImage `
        pg_restore --host=$dumpHost --port=$pgPort --username=$pgUser --dbname=$TargetDatabase `
            --clean --if-exists --no-owner --no-privileges --exit-on-error $tmp
    $code = $LASTEXITCODE
}

if ($code -ne 0) { Write-Error "pg_restore failed with exit code $code." }

# --- prove it ------------------------------------------------------------------------------------
# Plain count(*) on lowercase table names only: no quoted identifiers, because passing embedded double
# quotes through PowerShell to a native command is its own category of bug and this is not the place for it.
Write-Host "== What came back" -ForegroundColor Cyan
$checks = @(
    @{ Label = "accounts";          Sql = "SELECT count(*) FROM users" },
    @{ Label = "ranked coaches";    Sql = "SELECT count(*) FROM ranked_coaches" },
    @{ Label = "ranked fixtures";   Sql = "SELECT count(*) FROM ranked_fixtures" },
    @{ Label = "admin audit";       Sql = "SELECT count(*) FROM admin_audit" },
    @{ Label = "balance revisions"; Sql = "SELECT count(*) FROM balance_revisions" }
)

$failed = 0
foreach ($c in $checks) {
    $value = Invoke-Psql $TargetDatabase $c.Sql
    if ($value -match '^\d+$') {
        Write-Host ("   {0,-18} {1}" -f $c.Label, $value) -ForegroundColor Green
    } else {
        Write-Host ("   {0,-18} FAILED: {1}" -f $c.Label, $value) -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
if ($failed -gt 0) {
    Write-Error "$failed check(s) could not be read back - the restore is NOT verified."
}
Write-Host "Restore verified into $TargetDatabase." -ForegroundColor Green
Write-Host "Compare these numbers against GET /admin/metrics on the running server." -ForegroundColor Yellow
