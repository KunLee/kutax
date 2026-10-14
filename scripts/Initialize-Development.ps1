#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $repository 'infra/.env'
$apiProject = Join-Path $repository 'backend/KuTax.Api'

function New-LocalSecret([int] $Bytes = 24) {
    [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes)).ToLowerInvariant()
}

if (-not (Test-Path -LiteralPath $environmentFile)) {
    $localValues = [ordered]@{
        POSTGRES_PASSWORD = New-LocalSecret
        RUSTFS_ACCESS_KEY = New-LocalSecret 12
        RUSTFS_SECRET_KEY = New-LocalSecret
        DEVELOPMENT_SEED_PASSWORD = (New-LocalSecret) + 'Aa1!'
    }
    $lines = $localValues.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }
    [IO.File]::WriteAllLines($environmentFile, [string[]] $lines)
} else {
    $localValues = @{}
    foreach ($line in Get-Content -LiteralPath $environmentFile) {
        if ($line.Trim() -and -not $line.TrimStart().StartsWith('#')) {
            $parts = $line -split '=', 2
            if ($parts.Count -ne 2) { throw 'Invalid infra/.env line; expected KEY=value.' }
            $localValues[$parts[0].Trim()] = $parts[1].Trim()
        }
    }
}

foreach ($key in @('POSTGRES_PASSWORD', 'RUSTFS_ACCESS_KEY', 'RUSTFS_SECRET_KEY', 'DEVELOPMENT_SEED_PASSWORD')) {
    if (-not $localValues[$key]) { throw "Set $key in infra/.env, or remove the empty example before running this script." }
}
# These development values are reused in Compose interpolation and a connection string.
foreach ($key in @('POSTGRES_PASSWORD', 'RUSTFS_ACCESS_KEY', 'RUSTFS_SECRET_KEY')) {
    if ($localValues[$key] -notmatch '^[A-Za-z0-9_-]{16,}$') { throw "$key must contain at least 16 letters/digits/underscores/hyphens for this local setup helper." }
}

$secrets = @{
    'ConnectionStrings:Database' = 'Host=localhost;Database=kutax;Username=kutax;Password=' + $localValues['POSTGRES_PASSWORD']
    'Storage:AccessKey' = $localValues['RUSTFS_ACCESS_KEY']
    'Storage:SecretKey' = $localValues['RUSTFS_SECRET_KEY']
    'DevelopmentSeed:Password' = $localValues['DEVELOPMENT_SEED_PASSWORD']
}
$secrets | ConvertTo-Json -Compress | & dotnet user-secrets set --project $apiProject | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure .NET development user secrets.' }
Write-Host 'Local configuration is ready. Credentials are in ignored infra/.env and .NET user secrets.'
Write-Host 'After seeding, sign in as demo@kutax.test with DEVELOPMENT_SEED_PASSWORD from infra/.env.'
