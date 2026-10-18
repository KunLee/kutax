#Requires -Version 7
<#
.SYNOPSIS
  Lets GitHub Actions deploy KuTax to Cloud Run without any stored key. Run once, after the first deploy by hand.

.DESCRIPTION
  1. Creates a Workload Identity pool and GitHub provider that accept only workflows from this repository's main
     branch. GitHub proves who it is with a short-lived token; there is no JSON key to leak.
  2. Creates the kutax-deployer service account with just what a release needs: build images (Cloud Build), deploy
     the service and setup job (Cloud Run), and run them as kutax-run. It can't read the app's secrets, except the
     Cloudflare Worker's shared secret, used to check a release directly on Cloud Run.
  3. Stores the non-secret settings the workflow needs as GitHub repository variables (with the gh CLI, if signed in)
     or prints them to add by hand.
  Safe to run again.

.EXAMPLE
  ./scripts/Setup-GitHubDeploy.ps1 -ProjectId my-kutax -StorageEndpoint https://<account>.r2.cloudflarestorage.com -StorageBucket kutax-media
#>
param(
  [Parameter(Mandatory)] [string] $ProjectId,
  [Parameter(Mandatory)] [string] $StorageEndpoint,
  [string] $StorageBucket = 'kutax-private',
  [string] $Repository = 'KunLee/kutax',
  [string] $Branch = 'main',
  [string] $Region = 'australia-southeast1'
)
$ErrorActionPreference = 'Stop'
function Invoke-Gcloud { & gcloud @args; if ($LASTEXITCODE) { throw "gcloud $($args -join ' ') failed ($LASTEXITCODE)." } }
function Test-Gcloud { & gcloud @args 2>$null | Out-Null; return $LASTEXITCODE -eq 0 }

$projectNumber = (& gcloud projects describe $ProjectId --format 'value(projectNumber)').Trim()
if (-not $projectNumber) { throw "Can't read project $ProjectId. Sign in with gcloud auth login first." }
$runAccount = "kutax-run@$ProjectId.iam.gserviceaccount.com"
if (-not (Test-Gcloud iam service-accounts describe $runAccount --project $ProjectId)) {
  throw 'Deploy by hand once first (scripts/Deploy-CloudRun.ps1): it sets up the project, secrets and service account.'
}

Write-Host '1/3 GitHub sign-in (Workload Identity Federation)'
Invoke-Gcloud services enable iamcredentials.googleapis.com sts.googleapis.com iam.googleapis.com cloudresourcemanager.googleapis.com --project $ProjectId
if (-not (Test-Gcloud iam workload-identity-pools describe github --location global --project $ProjectId)) {
  Invoke-Gcloud iam workload-identity-pools create github --location global --display-name 'GitHub Actions' --project $ProjectId
}
# Only this repository's main branch can sign in, whatever the workflow.
$condition = "assertion.repository == '$Repository' && assertion.ref == 'refs/heads/$Branch'"
$mapping = 'google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref'
if (Test-Gcloud iam workload-identity-pools providers describe github-actions --workload-identity-pool github --location global --project $ProjectId) {
  Invoke-Gcloud iam workload-identity-pools providers update-oidc github-actions --workload-identity-pool github --location global `
    --attribute-mapping $mapping --attribute-condition $condition --project $ProjectId
} else {
  Invoke-Gcloud iam workload-identity-pools providers create-oidc github-actions --workload-identity-pool github --location global `
    --issuer-uri 'https://token.actions.githubusercontent.com' --attribute-mapping $mapping --attribute-condition $condition --project $ProjectId
}
$provider = "projects/$projectNumber/locations/global/workloadIdentityPools/github/providers/github-actions"

Write-Host '2/3 Deploying account (build and deploy only)'
$deployer = "kutax-deployer@$ProjectId.iam.gserviceaccount.com"
if (-not (Test-Gcloud iam service-accounts describe $deployer --project $ProjectId)) {
  Invoke-Gcloud iam service-accounts create kutax-deployer --display-name 'KuTax GitHub deploys' --project $ProjectId
}
# Viewer lets it follow the build log and check that secrets exist (not read them); the rest builds and deploys.
foreach ($role in 'roles/run.admin', 'roles/cloudbuild.builds.editor', 'roles/viewer', 'roles/serviceusage.serviceUsageConsumer') {
  Invoke-Gcloud projects add-iam-policy-binding $ProjectId --member "serviceAccount:$deployer" --role $role --condition None | Out-Null
}
# Uploading the source for Cloud Build (the bucket exists after the first deploy).
Invoke-Gcloud storage buckets add-iam-policy-binding "gs://${ProjectId}_cloudbuild" --member "serviceAccount:$deployer" --role roles/storage.objectAdmin | Out-Null
# Running the service and setup job as kutax-run, and builds as the default build account.
foreach ($runAs in $runAccount, "$projectNumber-compute@developer.gserviceaccount.com") {
  Invoke-Gcloud iam service-accounts add-iam-policy-binding $runAs --member "serviceAccount:$deployer" --role roles/iam.serviceAccountUser --project $ProjectId | Out-Null
}
# Checking a release on your own domain: the deploy asks Cloud Run directly with the Worker's secret (this one secret
# only; it can't read the database or storage credentials).
if (Test-Gcloud secrets describe kutax-origin-secret --project $ProjectId) {
  Invoke-Gcloud secrets add-iam-policy-binding kutax-origin-secret --member "serviceAccount:$deployer" --role roles/secretmanager.secretAccessor --project $ProjectId | Out-Null
}
Invoke-Gcloud iam service-accounts add-iam-policy-binding $deployer --role roles/iam.workloadIdentityUser `
  --member "principalSet://iam.googleapis.com/projects/$projectNumber/locations/global/workloadIdentityPools/github/attribute.repository/$Repository" --project $ProjectId | Out-Null

Write-Host '3/3 GitHub repository variables (not secrets: none of these grant access by themselves)'
$variables = [ordered]@{
  GCP_PROJECT_ID = $ProjectId
  GCP_WORKLOAD_IDENTITY_PROVIDER = $provider
  GCP_DEPLOY_SERVICE_ACCOUNT = $deployer
  KUTAX_STORAGE_ENDPOINT = $StorageEndpoint
  KUTAX_STORAGE_BUCKET = $StorageBucket
}
$signedIn = $false
if (Get-Command gh -ErrorAction SilentlyContinue) { & gh auth status *> $null; $signedIn = $LASTEXITCODE -eq 0 }
if ($signedIn) {
  foreach ($entry in $variables.GetEnumerator()) {
    & gh variable set $entry.Key --repo $Repository --body $entry.Value
    if ($LASTEXITCODE) { throw "Couldn't set GitHub variable $($entry.Key)." }
  }
  Write-Host "Set on $Repository."
} else {
  Write-Host "Add these under GitHub → $Repository → Settings → Secrets and variables → Actions → Variables:"
  $variables.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key) = $($_.Value)" }
}
Write-Host 'Optional variables, to match what you pass to Deploy-CloudRun.ps1 by hand: KUTAX_PUBLIC_ORIGIN, KUTAX_ALLOW_REGISTRATION (true),'
Write-Host 'KUTAX_SMTP_HOST, KUTAX_SMTP_PORT, KUTAX_SMTP_USERNAME, KUTAX_EMAIL_FROM.'
