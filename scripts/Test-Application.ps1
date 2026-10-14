# Requires PowerShell 7. Run only against the synthetic development workspace.
[CmdletBinding()]
param(
    [string] $BaseUri = 'http://localhost:5080',
    [string] $EnvironmentFile = (Join-Path $PSScriptRoot '../infra/.env')
)

$ErrorActionPreference = 'Stop'
$settings = @{}
Get-Content -LiteralPath $EnvironmentFile | ForEach-Object {
    if ($_ -match '^([^#=]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
if (-not $settings['DEVELOPMENT_SEED_PASSWORD']) { throw 'Development seed password is missing.' }
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$BaseUri = $BaseUri.TrimEnd('/')

function Request([string] $Path, [int] $Expected, [string] $Method = 'GET', $Body = $null, [bool] $WithCsrf = $false) {
    $arguments = @{
        Uri = "$BaseUri$Path"; Method = $Method; WebSession = $session
        SkipHttpErrorCheck = $true; TimeoutSec = 20
    }
    if ($null -ne $Body) { $arguments.Body = $Body | ConvertTo-Json -Compress; $arguments.ContentType = 'application/json' }
    if ($WithCsrf) {
        $cookie = $session.Cookies.GetCookies([uri] $BaseUri)['XSRF-TOKEN']
        if ($null -eq $cookie) { throw 'CSRF cookie is missing.' }
        $arguments.Headers = @{ 'X-XSRF-TOKEN' = [uri]::UnescapeDataString($cookie.Value) }
    }
    $response = Invoke-WebRequest @arguments
    if ([int] $response.StatusCode -ne $Expected) { throw "$Method $Path expected $Expected, got $($response.StatusCode)." }
    return $response
}

$null = Request '/api/health/ready' 200
foreach ($path in @('/', '/workspace')) {
    $html = (Request $path 200).Content
    if ($html -notmatch '<app-root>') { throw "SPA assets are missing at $path." }
    if ($html -match '<script\b(?![^>]*\bsrc=)[^>]*>') { throw "Inline script at $path conflicts with the app's Content Security Policy." }
}
$null = Request '/api/unknown' 404
$null = Request '/api/auth/session' 401
$credentials = @{ email = 'demo@kutax.test'; password = $settings['DEVELOPMENT_SEED_PASSWORD'] }
$null = Request '/api/auth/login' 400 'POST' $credentials
$null = Request '/api/auth/csrf' 204
$null = Request '/api/auth/login' 204 'POST' $credentials $true
$account = (Request '/api/auth/session' 200).Content | ConvertFrom-Json
$originalYear = $account.financialYearStart
$targetYear = if ($originalYear -eq 2024) { 2025 } else { 2024 }
$null = Request '/api/auth/csrf' 204
$null = Request '/api/accounts/another-account' 404
$null = Request "/api/accounts/$($account.id)/financial-year" 400 'PUT' @{ startYear = $targetYear }
try {
    $saved = (Request "/api/accounts/$($account.id)/financial-year" 200 'PUT' @{ startYear = $targetYear } $true).Content | ConvertFrom-Json
    if ($saved.financialYearStart -ne $targetYear) { throw 'Financial year was not saved.' }
    $null = Request '/api/auth/logout' 204 'POST' $null $true
    $null = Request '/api/auth/session' 401
    $null = Request '/api/auth/csrf' 204
    $null = Request '/api/auth/login' 204 'POST' $credentials $true
    $persisted = (Request '/api/auth/session' 200).Content | ConvertFrom-Json
    if ($persisted.financialYearStart -ne $targetYear) { throw 'Financial year did not persist across sign-in.' }
}
finally {
    $null = Request '/api/auth/csrf' 204
    $null = Request "/api/accounts/$($account.id)/financial-year" 200 'PUT' @{ startYear = $originalYear } $true
    $null = Request '/api/auth/logout' 204 'POST' $null $true
}
Write-Host 'Application smoke checks passed: readiness, SPA, authentication, CSRF, ownership, persisted financial year and logout.'
