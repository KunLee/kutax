# KuTax

A private-first Australian tax-record and preparation app. Collect receipts, review exceptions and prepare a traceable annual package to help you complete myTax yourself.

**Current scope: M0 foundation plus user management.** Sign-in, an authenticated workspace, saved financial-year selection, administrator account management and personal settings are implemented. Receipt import, AI extraction, tax rules and annual exports are later milestones. Nothing is lodged with the ATO.

The product baseline is [the architecture handoff](docs/architecture/KUTAX_HANDOFF.md). See [implementation status and outstanding checks](docs/architecture/IMPLEMENTATION.md) and [foundation decisions](docs/adr/0001-foundation.md).

Use the latest stable releases, with reviewed version pins and lock files. The [dependency baseline](docs/architecture/DEPENDENCIES.md) records versions, upstream references and the Angular/TypeScript compatibility exception.

## Local quick start

The quickest way: with Podman (or Docker Desktop) installed, run

```powershell
./scripts/Start-Local.ps1
```

It starts PostgreSQL, RustFS, ClamAV and Mailpit; builds the app image; backs up the database to `infra/backups/` (newest 5 kept, git-ignored); applies migrations; prepares the bucket and demo account; starts the app; and opens http://localhost:5080. It creates `infra/.env` on the first run. Run it again after code changes. Options: `-NoBuild`, `-NoBrowser`, `-NoBackup`, and `-Stop` (stops the containers, data is kept). Local email (invitations) is at http://localhost:8025.

For development with live reload, use the steps below instead.


Prerequisites: **.NET SDK 10.0.402**, **Node.js 26.11.1** (see `frontend/.nvmrc`), **npm 12.2.0**, **Docker Engine/Desktop with Compose v2 or Podman with a Compose provider**, and **PowerShell 7** for the configuration helper. Run commands from the repository root. On macOS/Linux, run the helper with `pwsh -File ./scripts/Initialize-Development.ps1`. Install the pinned npm version with `npm install --global npm@12.2.0` after selecting Node.

With Podman, start its machine and replace `docker compose` below with `podman compose`. Check the provider with `podman compose version`. The complete local stack has been built and tested using Podman on Windows.

```powershell
./scripts/Initialize-Development.ps1
docker compose -f infra/compose.yaml up -d --wait postgres rustfs
dotnet tool restore
dotnet restore backend/KuTax.sln --locked-mode
dotnet run --project backend/KuTax.Api -- --migrate --initialise-storage --seed-development
dotnet run --project backend/KuTax.Api
```

In another terminal:

```powershell
cd frontend
npm ci
npm start
```

Open **http://localhost:4200**. Sign in as **`demo@kutax.test`**, using the `DEVELOPMENT_SEED_PASSWORD` value from **`infra/.env`**. The account and future development fixtures are synthetic; do not import real receipts into this foundation.

The setup helper generates independent random local credentials, writes ignored `infra/.env`, and configures .NET development user secrets. It reuses existing values on subsequent runs and does not print them. To configure manually, use `infra/.env.example` and set `ConnectionStrings:Database`, `Storage:AccessKey`, `Storage:SecretKey` and `DevelopmentSeed:Password` via .NET user secrets. The database connection is `Host=localhost;Database=kutax;Username=kutax;Password=<local password>`.

The API runs at `http://localhost:5080`; Angular proxies `/api` to it. PostgreSQL (`5432`), RustFS S3 (`9000`) and RustFS console (`9001`) bind only to loopback. The RustFS console is at `http://localhost:9001`. Bucket `kutax-private` is created by the explicit setup command; no public access policy is installed. The local S3 account is the RustFS development administrator; production needs scoped credentials.

Database migrations and bucket initialisation are explicit, repeatable commands, never automatic web-startup changes. Development seeding refuses to run outside the `Development` environment. Rerunning seeding keeps an existing account/password and grants the synthetic owner administrator access; changing the env file does not reset Identity passwords. Public registration and email-based recovery are closed.

`GET /api/health/live` checks the process; `GET /api/health/ready` checks database connectivity, applied migrations and authenticated bucket access. Readiness failures return HTTP 503 without private service details. A missing `wwwroot` warning is expected when running the API alongside the Angular dev server; the published app includes those assets.

## Checks

```powershell
dotnet restore backend/KuTax.sln --locked-mode
dotnet test --solution backend/KuTax.sln
dotnet ef migrations has-pending-model-changes --project backend/KuTax.Infrastructure --startup-project backend/KuTax.Api
npm --prefix frontend run build
npm --prefix frontend test
```

The API tests run real Identity login/cookies, ownership checks, antiforgery validation, lockout and financial-year boundary tests against isolated SQLite databases. They do not stand in for PostgreSQL or RustFS verification.

The three service integration tests explicitly skip without `KUTAX_INTEGRATION=1`. With dependencies started, provide explicit environment settings for portable test/CI runs, then run:

```powershell
$env:ConnectionStrings__Database = 'Host=localhost;Database=kutax;Username=kutax;Password=<POSTGRES_PASSWORD from infra/.env>'
$env:Storage__Endpoint = 'http://localhost:9000'
$env:Storage__AccessKey = '<RUSTFS_ACCESS_KEY from infra/.env>'
$env:Storage__SecretKey = '<RUSTFS_SECRET_KEY from infra/.env>'
$env:KUTAX_INTEGRATION = '1'
dotnet test --project backend/tests/KuTax.IntegrationTests/KuTax.IntegrationTests.csproj
```

These tests apply migrations, create/remove synthetic accounts, and upload/read/remove a synthetic storage object. They verify anonymous object/bucket-list denial and the administrator/member lifecycle. Use development/test services. Start the current application image first and set `$env:KUTAX_APP_URL = 'http://localhost:5080'` to test user management through its actual HTTP listener; otherwise that lifecycle uses the test host against the real services. CI builds/starts the image and runs the lifecycle through it.

## User management

Sign in as `demo@kutax.test` and open **Users**. If upgrading the foundation, rerun the explicit migration/seed command using the newly built image, then sign in again. The existing demo password and records are retained.

- Administrators can search/page accounts, create users, make or remove administrators, keep private notes, enable/disable access, clear password lockouts, set temporary passwords and delete empty accounts. Names and email addresses belong to each person: names change in personal settings, and a new address means a new account.
- Created accounts and password resets require the user to choose a different password at next sign-in. Workspace APIs are blocked until this is complete.
- **Personal settings** lets every user save their name and change their password using their current password. Other sessions are revoked on the next request.
- Account edits, disabling and password resets revoke existing sessions. Disabling preserves records. Password resets also clear temporary lockout. No passwords, hashes or reset tokens are returned in user-list responses.
- Administrator accounts are protected from these management actions. Their owners use Personal settings; administrator provisioning stays with the server operator. There is no account deletion, public registration, email verification/delivery, self-service forgotten-password flow or MFA in this slice.

For an independently provisioned administrator, apply migrations, supply `BootstrapAdmin__Email` and `BootstrapAdmin__Password` through protected configuration, then run the application with `--bootstrap-admin`. For example, on an appropriately configured host:

```powershell
dotnet run --project backend/KuTax.Api -- --migrate --bootstrap-admin
```

For the published image, pass `--bootstrap-admin` to its entrypoint with the same configuration. A new administrator must change the initial password at first sign-in. Granting administrator access to an existing account requires that account's current password and enabled status. The command does not reset existing passwords. Remove bootstrap credentials from runtime configuration afterward. The local demo seed is restricted to Development and is separate from this operator command.

Successful administrator mutations emit structured server logs containing actor/target IDs and the action, without credential payloads. These logs are not the durable audit-history feature planned for later milestones.

## Sign-up

When `Registration:Enabled` is true, the sign-in screen offers **Create account**. It is on for local development and the local Compose app, and off by default elsewhere. New accounts are ordinary members with every workspace feature. Administrator access still comes from `--bootstrap-admin` or an existing administrator. See [ADR 0005](docs/adr/0005-self-registration.md) for the limits: no email verification and no CAPTCHA.

## AI receipt reading (Groq or OpenAI)

1. In the Groq console, turn on **Zero Data Retention** under Data Controls.
2. As an administrator, open **Settings → AI provider**:
   - choose the provider and paste the API key;
   - save, then use **Check key & load models**;
   - pick the photo and PDF text models, review the region and retention notice, tick **Read receipts with AI** and save.

   The key is encrypted with the app's data-protection keys and is never shown again. Saved settings override `infra/.env`. Until an administrator saves, the `AI_*` values in `infra/.env` (see `infra/.env.example`) apply.
3. Each user opens **Settings → Receipt privacy and AI** and gives consent. Changing the provider, a model, the region or the notice asks everyone to consent again.

If the provider rate-limits KuTax, receipts wait and retry automatically. If a wait over 10 minutes is requested, such as for an exhausted daily quota, those receipts fall back to manual entry.

Photos go to the vision model. PDFs send their text layer to the text model. Scanned PDFs fall back to manual entry. See [ADR 0006](docs/adr/0006-groq-extraction.md).

## Run the application image locally

The same Compose file has an optional `app` profile. Stop the local API first to free port 5080:

```powershell
docker compose -f infra/compose.yaml --profile app build app
docker compose -f infra/compose.yaml --profile app run --rm app --migrate --initialise-storage --seed-development
docker compose -f infra/compose.yaml --profile app up -d app
./scripts/Test-Application.ps1
```

Open **http://localhost:5080**. This image contains Angular static assets and ASP.NET Core, with no Node runtime. The optional Compose profile deliberately uses `Development` for local HTTP sign-in. The image itself defaults to production. Production needs TLS termination, the deployed `AllowedHosts`, a protected persistent data-protection key directory, production account provisioning, scoped S3 credentials and the remaining M6 operational/security work. This is not a production launch configuration.

The application smoke script uses the synthetic account from `infra/.env` and checks readiness, SPA routes, sign-in/logout, CSRF, ownership and financial-year persistence through the running image. It restores the account's original year. CI runs it after building the image. Production builds disable Angular critical-CSS inlining to avoid inline scripts under the app's `script-src 'self'` policy; see [Angular's CSP guidance](https://angular.dev/best-practices/security#content-security-policy). Local container database connections disable the unused Kerberos negotiation described in [Npgsql's documentation](https://www.npgsql.org/doc/security.html#gss-session-encryption-gss-api).

For a TLS-terminating reverse proxy, forward `X-Forwarded-Proto` and configure its exact IP with `ReverseProxy__KnownProxies__0` (additional addresses use `__1`, etc.). Framework loopback trust remains enabled; arbitrary remote proxies are not trusted. Keep the app listener private behind that proxy. Production sign-in requires an HTTPS request, directly or through a trusted proxy.

```powershell
docker compose -f infra/compose.yaml --profile app down
```

Named volumes retain the database, objects and cookie-protection keys. `down --volumes` deletes them; do not use it to stop a workspace you want to keep. Do not rotate the PostgreSQL env password without updating the existing database role.

## Backup and restore drill

```powershell
./scripts/Test-Recovery.ps1
```

Requires PowerShell 7, Podman and the running KuTax Compose stack. The script briefly stops the app and RustFS so no writes happen during the backup, runs `--verify-recovery` against the source, then writes a `pg_dump`, a RustFS volume archive, the data-protection keys and a SHA-256 manifest to a permission-restricted folder under `artifacts/recovery/`. It restores everything into a new isolated network and fresh volumes and reruns `--verify-recovery`. The drill passes only if both results match exactly: every document and export object hash, snapshot reconciliation and evidence references, and record counts. Any original containers it stopped are restarted afterwards; the stopped `kutax-restore-*` containers and volumes are kept for inspection and can be removed with `podman rm` / `podman volume rm`.

Backups contain every receipt and the cookie-protection keys, and KuTax does not encrypt them. Keep them on encrypted storage. See [THREAT_MODEL.md](docs/architecture/THREAT_MODEL.md) for the remaining operational risks.

## Extraction accuracy evaluation

`--generate-evaluation-fixtures` writes 100 labelled synthetic receipt PDFs and `labels.json` to `Evaluation:OutputDirectory` (default `artifacts/extraction-evaluation`). It makes no external calls. `--evaluate-synthetic` sends those synthetic PDFs to the configured AI provider and writes `accuracy.json` with merchant, date and amount accuracy measured separately against the ≥95% target. It refuses to run unless the provider, model, region and retention notice are configured and `Evaluation:ExternalAiConsent=true` is set explicitly. Results cover machine-readable synthetic PDFs only, not photographed or real receipts.

## Repository

```text
frontend/                      Angular SPA
backend/KuTax.Api/             HTTP host, account/auth features, setup commands
backend/KuTax.Core/            Infrastructure-independent domain types
backend/KuTax.Infrastructure/  EF Core/Identity, PostgreSQL migrations, S3 connectivity
backend/tests/                API/unit tests and opt-in service integration tests
infra/                        Persistent local dependencies and production Dockerfile
scripts/                      Development setup, application smoke test and recovery drill
docs/architecture/            Product baseline and implementation tracking
docs/adr/                     Material implementation decisions
```
