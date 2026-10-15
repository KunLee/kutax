#Requires -Version 7
<#
.SYNOPSIS
  Deploys KuTax to Google Cloud Run: the app with ClamAV beside it, PostgreSQL on Supabase, files on Cloudflare R2.

.DESCRIPTION
  Safe to run again for every release. It:
    1. enables the Google APIs it needs and creates the image repository and a service account (first run only);
    2. stores credentials in Secret Manager, asking for any that are missing (they are never printed or written to disk);
    3. builds both images in Cloud Build (rebuilding refreshes ClamAV's signatures);
    4. runs database migrations and storage setup as a one-off Cloud Run job;
    5. deploys the service (scales to zero when idle) and opens it to the web (the app has its own sign-in).
  See docs/operations/cloud-run.md for the Supabase and Cloudflare R2 steps that come first.

.EXAMPLE
  ./scripts/Deploy-CloudRun.ps1 -ProjectId my-kutax -StorageEndpoint https://<account>.r2.cloudflarestorage.com
.EXAMPLE
  ./scripts/Deploy-CloudRun.ps1 -ProjectId my-kutax -StorageEndpoint https://<account>.r2.cloudflarestorage.com -AdminEmail me@example.com
.EXAMPLE
  ./scripts/Deploy-CloudRun.ps1 -ProjectId my-kutax -StorageEndpoint https://<account>.r2.cloudflarestorage.com -Unattended
  What GitHub Actions runs (.github/workflows/deploy.yml): never prompts, and needs the first deploy done by hand.
#>
param(
  [Parameter(Mandatory)] [string] $ProjectId,
  [Parameter(Mandatory)] [string] $StorageEndpoint,
  [string] $StorageBucket = 'kutax-private',
  [string] $Region = 'australia-southeast1',
  [string] $Service = 'kutax',
  # Your own domain once it's mapped to the service; defaults to the service's run.app address.
  [string] $PublicOrigin = '',
  [switch] $AllowRegistration,
  # Creates the first administrator on this run (asks for a temporary password).
  [string] $AdminEmail = '',
  # Optional: SMTP for sharing invitations (any provider, e.g. Resend, Brevo, Amazon SES). Asks for the password once.
  [string] $SmtpHost = '',
  [int] $SmtpPort = 587,
  [string] $SmtpUsername = '',
  [string] $EmailFrom = '',
  # For CI: never asks for anything. Project setup and secrets must already exist (the first deploy is run by hand), and
  # the deploying account only needs to build and deploy, not manage secrets or APIs.
  [switch] $Unattended
)
$ErrorActionPreference = 'Stop'
function Invoke-Gcloud { & gcloud @args; if ($LASTEXITCODE) { throw "gcloud $($args -join ' ') failed ($LASTEXITCODE)." } }
function Test-Gcloud { & gcloud @args 2>$null | Out-Null; return $LASTEXITCODE -eq 0 }
# Hidden input; asks again when it's empty or too short, and (with -Confirm) until it's typed the same twice.
# Pasting: right-click or Ctrl+V in Windows Terminal; nothing is shown either way.
function Read-Secret([string] $prompt, [int] $MinLength = 1, [switch] $Confirm) {
  while ($true) {
    $value = [Net.NetworkCredential]::new('', (Read-Host -AsSecureString $prompt)).Password.Trim()
    if ($value.Length -lt $MinLength) { Write-Host "  Nothing was entered, or it's shorter than $MinLength characters. Try again." -ForegroundColor Yellow; continue }
    if ($Confirm -and $value -ne [Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'Type it again to confirm')).Password.Trim()) {
      Write-Host "  The two entries don't match. Try again." -ForegroundColor Yellow; continue
    }
    return $value
  }
}
function Set-Secret([string] $name, [string] $value) {
  if (-not (Test-Gcloud secrets describe $name --project $ProjectId)) { Invoke-Gcloud secrets create $name --replication-policy=automatic --project $ProjectId | Out-Null }
  $file = New-TemporaryFile
  try { [IO.File]::WriteAllText($file, $value); Invoke-Gcloud secrets versions add $name --data-file=$file --project $ProjectId | Out-Null }
  finally { Remove-Item $file -Force }
}
function Confirm-Secret([string] $name, [scriptblock] $create) {
  if ($Unattended) {
    if (-not (Test-Gcloud secrets describe $name --project $ProjectId)) { throw "Secret $name is missing. Run this script by hand once to store it." }
    return
  }
  if (Test-Gcloud secrets versions access latest --secret $name --project $ProjectId) { return }
  Set-Secret $name (& $create)
}
if ($Unattended -and $AdminEmail) { throw 'Create the first administrator in a deploy run by hand, not with -Unattended.' }
$account = "kutax-run@$ProjectId.iam.gserviceaccount.com"

Write-Host "1/5 Project setup ($ProjectId, $Region)"
if ($Unattended) {
  if (-not (Test-Gcloud artifacts repositories describe kutax --location $Region --project $ProjectId) -or -not (Test-Gcloud iam service-accounts describe $account --project $ProjectId)) {
    throw 'The project is not set up yet. Run this script by hand once before deploying from CI.'
  }
} else {
Invoke-Gcloud services enable run.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com secretmanager.googleapis.com --project $ProjectId
if (-not (Test-Gcloud artifacts repositories describe kutax --location $Region --project $ProjectId)) {
  Invoke-Gcloud artifacts repositories create kutax --repository-format=docker --location $Region --project $ProjectId
}
if (-not (Test-Gcloud iam service-accounts describe $account --project $ProjectId)) {
  Invoke-Gcloud iam service-accounts create kutax-run --display-name 'KuTax Cloud Run' --project $ProjectId
}
}

$projectNumber = (& gcloud projects describe $ProjectId --format 'value(projectNumber)').Trim()
$runHost = "$Service-$projectNumber.$Region.run.app"
if (-not $PublicOrigin) { $PublicOrigin = "https://$runHost" }
$publicHost = ([Uri]$PublicOrigin).Host
# Your own domain goes through the Cloudflare Worker (infra/cloudflare), which reaches Cloud Run by its run.app name,
# so both names are allowed (never wildcards) and the Worker proves itself with a shared secret.
$customDomain = $publicHost -ne $runHost
$allowedHosts = if ($customDomain) { "$publicHost;$runHost" } else { $publicHost }

Write-Host '2/5 Secrets (asks only for ones not stored yet)'
Confirm-Secret kutax-database { Read-Secret 'Supabase connection string (session pooler, port 5432, ending ;SSL Mode=Require)' -MinLength 40 }
Confirm-Secret kutax-storage-access-key { Read-Secret 'Cloudflare R2 access key ID' -MinLength 16 }
Confirm-Secret kutax-storage-secret-key { Read-Secret 'Cloudflare R2 secret access key' -MinLength 24 }
# Encrypts the sign-in keys KuTax keeps in the database. Generated here once; keep it: replacing it signs everyone out
# and makes saved AI keys unreadable.
if ($Unattended) {
  Confirm-Secret kutax-keys-certificate {}
  Confirm-Secret kutax-keys-certificate-password {}
} elseif (-not (Test-Gcloud secrets versions access latest --secret kutax-keys-certificate --project $ProjectId)) {
  $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
  $rsa = [Security.Cryptography.RSA]::Create(3072)
  $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=KuTax data protection', $rsa, 'SHA256', [Security.Cryptography.RSASignaturePadding]::Pkcs1)
  $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(20))
  Set-Secret kutax-keys-certificate ([Convert]::ToBase64String($certificate.Export('Pfx', $password)))
  Set-Secret kutax-keys-certificate-password $password
}
$secrets = @('kutax-database', 'kutax-storage-access-key', 'kutax-storage-secret-key', 'kutax-keys-certificate', 'kutax-keys-certificate-password')
if ($customDomain) {
  # Generated once and shared with the Worker (see docs/operations/cloud-run.md); never printed.
  Confirm-Secret kutax-origin-secret { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant() }
  $secrets += 'kutax-origin-secret'
}
if ($SmtpHost) {
  if (-not $EmailFrom) { throw 'Give -EmailFrom (the sender address) with -SmtpHost.' }
  Confirm-Secret kutax-smtp-password { Read-Secret "SMTP password for $SmtpUsername@$SmtpHost" -MinLength 8 }
  $secrets += 'kutax-smtp-password'
}
foreach ($name in $(if ($Unattended) { @() } else { $secrets })) {
  Invoke-Gcloud secrets add-iam-policy-binding $name --member "serviceAccount:$account" --role roles/secretmanager.secretAccessor --project $ProjectId | Out-Null
}

Write-Host '3/5 Building images in Cloud Build'
# Images are tagged with the time, and the commit when deployed from CI.
$tag = (Get-Date -Format 'yyyyMMdd-HHmmss') + $(if ($env:GITHUB_SHA) { '-' + $env:GITHUB_SHA.Substring(0, 7) } else { '' })
$registry = "$Region-docker.pkg.dev/$ProjectId/kutax"
$appImage = "$registry/app:$tag"; $clamavImage = "$registry/clamav:$tag"
Invoke-Gcloud builds submit --config infra/cloudrun/cloudbuild.yaml --substitutions "_APP_IMAGE=$appImage,_CLAMAV_IMAGE=$clamavImage" --project $ProjectId .

$shared = "ASPNETCORE_ENVIRONMENT=Production,PublicOrigin=$PublicOrigin,AllowedHosts=$allowedHosts,Production__ManagedServices=true,DataProtection__Store=Database,Storage__Endpoint=$StorageEndpoint,Storage__Bucket=$StorageBucket,Storage__Region=auto,Scanning__Host=localhost,Processing__Enabled=false"
$secretEnv = 'ConnectionStrings__Database=kutax-database:latest,Storage__AccessKey=kutax-storage-access-key:latest,Storage__SecretKey=kutax-storage-secret-key:latest,DataProtection__CertificateBase64=kutax-keys-certificate:latest,DataProtection__CertificatePassword=kutax-keys-certificate-password:latest'
if ($customDomain) { $secretEnv += ',Origin__SharedSecret=kutax-origin-secret:latest' }

Write-Host '4/5 Database migrations and storage setup'
$setupArgs = '--migrate,--initialise-storage'
if ($AdminEmail) {
  Set-Secret kutax-bootstrap-password (Read-Secret "Temporary password for $AdminEmail (12+ characters with upper and lower case, a number and a symbol; you'll change it at first sign-in)" -MinLength 12 -Confirm)
  Invoke-Gcloud secrets add-iam-policy-binding kutax-bootstrap-password --member "serviceAccount:$account" --role roles/secretmanager.secretAccessor --project $ProjectId | Out-Null
  $setupArgs += ',--bootstrap-admin'
  $shared += ",BootstrapAdmin__Email=$AdminEmail"
  $secretEnv += ',BootstrapAdmin__Password=kutax-bootstrap-password:latest'
}
Invoke-Gcloud run jobs deploy "$Service-setup" --image $appImage --region $Region --project $ProjectId --service-account $account `
  "--args=$setupArgs" --set-env-vars $shared --set-secrets $secretEnv --max-retries 0 --task-timeout 600s
Invoke-Gcloud run jobs execute "$Service-setup" --region $Region --project $ProjectId --wait
if ($AdminEmail) { Invoke-Gcloud secrets delete kutax-bootstrap-password --quiet --project $ProjectId }

Write-Host '5/5 Deploying the service'
$values = @{ SERVICE = $Service; REGION = $Region; SERVICE_ACCOUNT = $account; APP_IMAGE = $appImage; CLAMAV_IMAGE = $clamavImage;
  PUBLIC_ORIGIN = $PublicOrigin; PUBLIC_HOST = $allowedHosts; STORAGE_ENDPOINT = $StorageEndpoint; STORAGE_BUCKET = $StorageBucket
  REGISTRATION_ENABLED = $(if ($AllowRegistration) { 'true' } else { 'false' })
  ORIGIN_ENV = $(if ($customDomain) { @"
            - name: Origin__SharedSecret
              valueFrom:
                secretKeyRef: { name: kutax-origin-secret, key: latest }
"@.TrimEnd() } else { '' })
  EMAIL_ENV = $(if ($SmtpHost) { @"
            - { name: Email__Host, value: "$SmtpHost" }
            - { name: Email__Port, value: "$SmtpPort" }
            - { name: Email__Username, value: "$SmtpUsername" }
            - { name: Email__From, value: "$EmailFrom" }
            - name: Email__Password
              valueFrom:
                secretKeyRef: { name: kutax-smtp-password, key: latest }
"@.TrimEnd() } else { '' }) }
$yaml = Get-Content infra/cloudrun/service.yaml -Raw
foreach ($key in $values.Keys) { $yaml = $yaml.Replace('${' + $key + '}', $values[$key]) }
if ($yaml -match '\$\{[A-Z_]+\}') { throw "Unfilled placeholder in service.yaml: $($Matches[0])" }
$rendered = New-TemporaryFile
try {
  [IO.File]::WriteAllText($rendered, $yaml)
  Invoke-Gcloud run services replace $rendered --region $Region --project $ProjectId
} finally { Remove-Item $rendered -Force }
Invoke-Gcloud run services add-iam-policy-binding $Service --member allUsers --role roles/run.invoker --region $Region --project $ProjectId | Out-Null

# The release counts only once the new revision answers: database, migrations and the bucket all checked. With your
# own domain, a deploy by hand first asks Cloud Run directly (with the Worker's secret), then through Cloudflare.
function Wait-Ready([string] $Url, [hashtable] $Headers = @{}) {
  $deadline = (Get-Date).AddMinutes(4)
  while ((Get-Date) -lt $deadline) {
    try { if ((Invoke-WebRequest -Uri "$Url/api/health/ready" -Headers $Headers -TimeoutSec 30 -SkipHttpErrorCheck).StatusCode -eq 200) { return $true } } catch { }
    Start-Sleep -Seconds 5
  }
  return $false
}
Write-Host 'Checking the new release is ready'
if ($customDomain -and -not $Unattended) {
  $originSecret = (& gcloud secrets versions access latest --secret kutax-origin-secret --project $ProjectId)
  if (-not (Wait-Ready "https://$runHost" @{ 'X-KuTax-Origin-Verify' = $originSecret })) { throw "Deployed, but the new revision didn't answer /api/health/ready within 4 minutes. Check the service logs in Cloud Run." }
  if (-not (Wait-Ready $PublicOrigin)) {
    Write-Warning "KuTax is running, but $PublicOrigin doesn't reach it yet. Deploy the Cloudflare Worker with the shared secret (docs/operations/cloud-run.md, Your own domain)."
    return
  }
} elseif (-not (Wait-Ready $PublicOrigin)) {
  throw "Deployed, but $PublicOrigin/api/health/ready did not answer 200 within 4 minutes. Check the service logs in Cloud Run."
}
Write-Host "Done: $PublicOrigin"
