# Deploying KuTax to Google Cloud Run

KuTax runs as one Cloud Run service. The app container holds the Angular site and the API, and ClamAV runs beside it as a sidecar in the same instance. PostgreSQL is on Supabase and files are in Cloudflare R2. The service scales to zero when nobody is using it. While an instance is up, its CPU stays on (instance-based billing), so receipt reading, tax packs and rule drafting keep going between page requests. Work left queued resumes on the next visit.

Everything is in Australia: Cloud Run `australia-southeast1` (Sydney), Supabase `ap-southeast-2` (Sydney) and R2 with the Oceania location hint.

## Before the first deploy

You need:
- a Google Cloud project with billing enabled, and the [gcloud CLI](https://cloud.google.com/sdk/docs/install) signed in (`gcloud auth login`);
- PowerShell 7.

### 1. Supabase (PostgreSQL)

1. Create a project in the **Sydney** region and give it a long generated database password. Use **Pro**: free projects pause after a week without activity, which isn't suitable for tax records.
2. Go to *Connect* → **Session pooler** and copy the connection details. Cloud Run reaches the internet over IPv4, and Supabase's direct connection is IPv6-only. Don't use the transaction pooler on port 6543: it breaks prepared statements.
3. Write it as an Npgsql connection string:
   ```
   Host=aws-0-ap-southeast-2.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<password>;SSL Mode=Require
   ```
   KuTax refuses to start in production without `SSL Mode=Require` or stricter.
4. Turn on Supabase's backups (daily backups come with Pro; point-in-time recovery is an add-on).

### 2. Cloudflare R2 (files)

1. Create a bucket named `kutax-private` with location hint **Oceania (OC)**. Keep public access **off**: KuTax serves files only to their signed-in owner.
2. Create an **API token** with *Object Read & Write*, limited to that bucket. Note the access key ID, the secret access key and the S3 endpoint `https://<account-id>.r2.cloudflarestorage.com`.

## Deploy (first time and every release)

```powershell
./scripts/Deploy-CloudRun.ps1 -ProjectId <your-project> -StorageEndpoint https://<account-id>.r2.cloudflarestorage.com -AdminEmail you@example.com
```

The first run asks for:
- the Supabase connection string;
- the R2 access key ID and secret access key;
- a temporary password for the first administrator.

It stores them in **Secret Manager**: they're never printed and never written to the repository. It also generates a certificate that encrypts KuTax's sign-in keys in the database. **Keep that secret (`kutax-keys-certificate`)**. Replacing it signs everyone out and makes the saved AI provider key unreadable, so you'd have to enter it again.

What happens on each run:
1. Cloud Build builds the app image and the ClamAV image. ClamAV's signatures are built into the image, so a cold start loads them from disk in seconds instead of downloading about 200 MB.
2. A one-off **Cloud Run job** applies database migrations and checks the bucket. With `-AdminEmail`, it also creates the first administrator, who must change the temporary password at first sign-in.
3. The service is deployed from `infra/cloudrun/service.yaml` and opened to the web. KuTax has its own sign-in, rate limits and CSRF protection.

**Releases:** later releases need only `-ProjectId` and `-StorageEndpoint`. Redeploy at least **weekly**, even without code changes, so ClamAV's built-in signatures never get old. A Cloud Scheduler trigger can do this for you.

**Your own domain:** map it in Cloud Run (*Manage custom domains*), then deploy again with `-PublicOrigin https://tax.example.com`. KuTax only answers on the hosts it's configured for.

**Email for sharing invitations (optional):** add `-SmtpHost smtp.example.com -SmtpUsername <user> -EmailFrom kutax@your-domain` (and `-SmtpPort` if it isn't 587). The SMTP password is asked for once and kept in Secret Manager. Any provider works (Resend, Brevo, Amazon SES, Google Workspace). Without it, KuTax shows the invitation link for you to send yourself.

**Sign-up:** add `-AllowRegistration` to let people create their own accounts. Without it, an administrator creates accounts.

## How it behaves

- **First visit after idle:** the page opens in a few seconds (the .NET app starts quickly). ClamAV needs another 10–40 seconds. Receipts uploaded in that time show "waiting for the malware check" and continue by themselves. Nothing skips the scan.
- **Background work:** reading, tax packs and rule drafting run while someone is using KuTax, and the inbox's progress checks keep the instance up while receipts are being read. If everyone leaves, the instance stops after about 15 minutes. Queued work is kept in the database and picks up again on the next visit.
- **Yearly tax rules:** checked only when someone opens a financial year that has no rules yet, from the June after that year ends. Each year is checked at most once a day. There is no timer.

## Costs (rough, check current pricing)

| Item | Approximate cost |
|---|---|
| Cloud Run (2 vCPU and 3 GiB, billed only while an instance is up) | A few dollars a month for personal use |
| Supabase Pro | US$25 a month |
| Cloudflare R2 | Free up to 10 GB, with no charge for downloads |
| Cloud Build, Artifact Registry, Secret Manager | Cents |

## Verified, and still to verify

- **Verified locally:**
  - Sign-in keys stored in PostgreSQL and encrypted with the certificate survive a container being deleted and recreated: the same cookies still sign in.
  - The baked ClamAV image answers within 9 seconds of starting.
  - The certificate made by the script loads the way the app loads it.
  - The storage changes (`Storage:Region`, SDK checksums only when required) pass all real-service tests against RustFS.
- **Not yet verified (needs your accounts):**
  - Cloud Run itself, including the sidecar memory limits and real cold-start times;
  - the Supabase session pooler;
  - uploads and downloads against real R2.

  Run a first deploy with synthetic data and check `/api/health/ready`, an upload, a download and a tax pack before storing real records.
