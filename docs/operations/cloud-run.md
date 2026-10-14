# Deploying KuTax to Google Cloud Run

KuTax runs as one Cloud Run service. The app container holds the Angular site and the API, and ClamAV runs beside it as a sidecar in the same instance. PostgreSQL is on Supabase and files are in Cloudflare R2. The service scales to zero when nobody is using it. While an instance is up, its CPU stays on (instance-based billing), so receipt reading, tax packs and rule drafting keep going between page requests. Work left queued resumes on the next visit.

Everything is in Australia: Cloud Run `australia-southeast1` (Sydney), Supabase `ap-southeast-2` (Sydney) and R2 with the Oceania location hint.

## Before the first deploy

You need:
- a Google Cloud project with billing enabled, and the [gcloud CLI](https://cloud.google.com/sdk/docs/install) signed in (`gcloud auth login`);
- PowerShell 7.

### 1. Supabase (PostgreSQL)

1. Create a project in the **Sydney** region and give it a generated database password of **at least 16 characters**, using letters and digits only (`;` or quotes would break the connection string). KuTax refuses to start in production with a shorter one. Use **Pro**: free projects pause after a week without activity, which isn't suitable for tax records.
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

**Releases:** later releases need only `-ProjectId` and `-StorageEndpoint`. ClamAV's signatures don't need a schedule. Each release builds them into the image, and when an instance starts, freshclam fetches whatever changed since then and keeps checking while the instance runs. After a long time without releases, that first catch-up is bigger; scans still run meanwhile with the built-in signatures.

**Your own domain:** map it in Cloud Run (*Manage custom domains*), then deploy again with `-PublicOrigin https://tax.example.com`. KuTax only answers on the hosts it's configured for.

**Email for sharing invitations (optional):** add `-SmtpHost smtp.example.com -SmtpUsername <user> -EmailFrom kutax@your-domain` (and `-SmtpPort` if it isn't 587). The SMTP password is asked for once and kept in Secret Manager. Any provider works (Resend, Brevo, Amazon SES, Google Workspace). Without it, KuTax shows the invitation link for you to send yourself.

**Sign-up:** add `-AllowRegistration` to let people create their own accounts. Without it, an administrator creates accounts.

## Continuous deployment (GitHub Actions)

After the first deploy by hand, releases go out by themselves. There are two long-lived branches: work goes to **`dev`**, and **`main`** is production. A release is a pull request from `dev` into `main`.
- `.github/workflows/ci.yml` (**Build and test**) runs on pushes to `dev` and `main` and on pull requests into `main`. It runs the backend, frontend and real-service tests and a smoke test of the app image. A pull request into `main` from any branch other than `dev` fails (`merge-source`).
- `.github/workflows/deploy.yml` (**Deploy to Cloud Run**) releases `main` to production when that run passes on a push. It deploys the exact commit that was tested, and can be started by hand (*Actions → Deploy to Cloud Run → Run workflow*). There's no schedule.

**One-time setup:**

```powershell
./scripts/Setup-GitHubDeploy.ps1 -ProjectId <your-project> -StorageEndpoint https://<account-id>.r2.cloudflarestorage.com -StorageBucket <bucket>
```

It sets up:
- **Workload Identity Federation:** GitHub signs in to Google Cloud with a short-lived token. No key is stored in GitHub, and the provider accepts only `KunLee/kutax` on `refs/heads/main`.
- **The `kutax-deployer` account**, which can only build and deploy:
  - Cloud Build editor;
  - Cloud Run admin;
  - project viewer (it can see that secrets exist but can't read them);
  - write access to the Cloud Build source bucket;
  - acting as `kutax-run` and the default build account.
- **GitHub repository variables:** with the `gh` CLI if you're signed in; otherwise it prints them for you to add.

If you deploy by hand with other options, set the matching variables so CI deploys the same way: `KUTAX_PUBLIC_ORIGIN`, `KUTAX_ALLOW_REGISTRATION=true`, `KUTAX_SMTP_HOST`, `KUTAX_SMTP_PORT`, `KUTAX_SMTP_USERNAME` and `KUTAX_EMAIL_FROM`.

**What CI can't do (on purpose):** store or change secrets, enable APIs, or create the first administrator. `Deploy-CloudRun.ps1 -Unattended` stops with a clear message if anything is missing; run the script by hand to add it. To change a secret (for example a new Supabase password), run the script by hand after deleting that secret in Secret Manager, or add a new version in the console.

**Protect `main`** (*Settings → Branches → Add rule* for `main`, or a ruleset):
- require a pull request before merging;
- require the status checks **foundation** and **merge-source** to pass;
- block force pushes and deletion.

Then the only way into production is a tested merge from `dev`.

**Gate releases (optional):** the workflow deploys to the GitHub environment `production`. Add *required reviewers* to it (*Settings → Environments → production*) to approve each release before it goes out.

Each release counts as done only when the new revision answers `GET /api/health/ready` (database, migrations and bucket) within 4 minutes. Otherwise the workflow fails. The previous revision keeps serving if the new one never became healthy.

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
