#Requires -Version 7.0
<#
.SYNOPSIS
Starts the whole KuTax stack locally in containers and opens it in the browser.

.DESCRIPTION
Starts PostgreSQL, RustFS storage, ClamAV and Mailpit, builds the app image (Angular and ASP.NET Core in one), backs up
the database, applies migrations, prepares the bucket and the synthetic demo account, then starts the app and waits
until it's ready. Safe to run again at any time: it picks up code changes and keeps your data.

Works with Podman (preferred when installed) or Docker Compose. Creates infra/.env on first run.

.EXAMPLE
./scripts/Start-Local.ps1
Builds and starts everything, then opens http://localhost:5080.

.EXAMPLE
./scripts/Start-Local.ps1 -NoBuild -NoBrowser
Starts what's already built without opening a browser.

.EXAMPLE
./scripts/Start-Local.ps1 -Stop
Stops every container. Data (database, files, sign-in keys) is kept.
#>
[CmdletBinding()]
param(
    # Use the app image already built instead of rebuilding it.
    [switch] $NoBuild,
    # Don't open the browser when it's ready.
    [switch] $NoBrowser,
    # Skip the database backup taken before migrations.
    [switch] $NoBackup,
    # Stop the containers instead (data is kept).
    [switch] $Stop
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repository 'infra/compose.yaml'
$environmentFile = Join-Path $repository 'infra/.env'
$backupDirectory = Join-Path $repository 'infra/backups'
$appUrl = 'http://localhost:5080'
$keepBackups = 5

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

# Podman first (the stack is tested with it), otherwise Docker Compose v2.
$engine = $null
if (Get-Command podman -ErrorAction SilentlyContinue) {
    $engine = 'podman'
    if ($IsWindows -or $IsMacOS) {
        $running = (& podman machine list --format '{{.Running}}' 2>$null) -contains 'true'
        if (-not $running) {
            Write-Step 'Starting the Podman machine'
            & podman machine start | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Could not start the Podman machine. Open Podman Desktop and try again.' }
        }
    }
} elseif (Get-Command docker -ErrorAction SilentlyContinue) {
    $engine = 'docker'
} else {
    throw 'Install Podman (with a Compose provider) or Docker Desktop first.'
}

function Invoke-Compose {
    & $engine compose -f $composeFile --env-file $environmentFile @args
    if ($LASTEXITCODE -ne 0) { throw "'$engine compose $($args -join ' ')' failed (exit code $LASTEXITCODE)." }
}

if ($Stop) {
    if (-not (Test-Path -LiteralPath $environmentFile)) { Write-Host 'Nothing to stop: infra/.env does not exist yet.'; return }
    Write-Step 'Stopping KuTax (data is kept)'
    Invoke-Compose --profile app stop
    Write-Host 'Stopped. Start again with ./scripts/Start-Local.ps1' -ForegroundColor Green
    return
}

if (-not (Test-Path -LiteralPath $environmentFile)) {
    Write-Step 'First run: creating local credentials (infra/.env)'
    & (Join-Path $PSScriptRoot 'Initialize-Development.ps1')
}

Write-Step 'Starting the database, storage, virus scanner and local mail'
Invoke-Compose up -d --wait postgres rustfs clamav mailpit

if (-not $NoBuild) {
    Write-Step 'Building the app image (this takes a few minutes the first time)'
    Invoke-Compose build app
}

if (-not $NoBackup) {
    # Before any migration: a restorable copy of the local database. Backups hold your records, so they stay out of git
    # (infra/backups is ignored) and only the newest few are kept.
    Write-Step 'Backing up the database'
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backup = Join-Path $backupDirectory ('kutax-{0:yyyyMMdd-HHmmss}.dump' -f (Get-Date))
    $container = (& $engine compose -f $composeFile --env-file $environmentFile ps -q postgres).Trim()
    if (-not $container) { throw 'The database container is not running.' }
    & $engine exec $container sh -c 'pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB" > /tmp/kutax-backup.dump'
    if ($LASTEXITCODE -ne 0) { throw 'The database backup failed; nothing was changed. Use -NoBackup to skip it.' }
    # A relative destination: "C:\..." would be read as a container named C.
    Push-Location $repository
    try { & $engine cp "${container}:/tmp/kutax-backup.dump" ([IO.Path]::GetRelativePath($repository, $backup).Replace('\', '/')) }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the database backup out of the container.' }
    & $engine exec $container rm -f /tmp/kutax-backup.dump | Out-Null
    Get-ChildItem -LiteralPath $backupDirectory -Filter 'kutax-*.dump' | Sort-Object Name -Descending | Select-Object -Skip $keepBackups | Remove-Item
    Write-Host "    saved $([IO.Path]::GetRelativePath($repository, $backup))"
}

Write-Step 'Updating the database, storage bucket and demo account'
Invoke-Compose run --rm app --migrate --initialise-storage --seed-development

Write-Step 'Starting the app'
Invoke-Compose up -d app

Write-Step 'Waiting until it is ready'
$deadline = (Get-Date).AddMinutes(2)
$ready = $false
while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest -Uri "$appUrl/api/health/ready" -UseBasicParsing -TimeoutSec 5 -SkipHttpErrorCheck
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch {
        # Still starting.
    }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw "KuTax didn't become ready. See the log with: $engine compose -f infra/compose.yaml --env-file infra/.env logs app" }

Write-Host ''
Write-Host "KuTax is running at $appUrl" -ForegroundColor Green
Write-Host '  Sign in as demo@kutax.test with DEVELOPMENT_SEED_PASSWORD from infra/.env'
Write-Host '  Local email (invitations): http://localhost:8025'
Write-Host '  Storage console:           http://localhost:9001'
Write-Host '  Stop with:                 ./scripts/Start-Local.ps1 -Stop'
if (-not $NoBrowser) { Start-Process $appUrl }
