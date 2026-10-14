# 0012 — Managed hosting on Cloud Run with Supabase and Cloudflare R2

Status: accepted, 13 October 2026

## Context

The owner wants the simplest way to run KuTax: one package, managed services, nothing running when nobody uses it. Background work (reading receipts, tax packs, yearly rule drafting) must therefore happen "just in time". The handoff's single app image, PostgreSQL and S3-compatible storage stay. Self-hosting on a VPS with `infra/compose.yaml` still works.

## Decision

- **Cloud Run, scaling to zero, with instance-based billing (CPU always allocated while an instance is up)**, at most one instance. Background services run while someone uses the app. Queued work survives in PostgreSQL and resumes on the next start.
- **Yearly rule checks are on demand.** Opening a year's summary or quick review requests a check for that year (`RuleCheckRequests`). The worker checks only a year newer than any rules in force, from the June after it ends, at most once every 20 hours. It otherwise sleeps until a request arrives or a paused draft's retry time comes. There is no timer.
- **ClamAV runs as a sidecar in the same instance**, because Cloud Run services can't reach each other over raw TCP. Signatures are baked into the image at build time (`infra/cloudrun/clamav.Dockerfile`), so cold starts load them from disk. `ConcurrentDatabaseReload no` keeps one signature set in memory. Scanning stays fail-closed, and the app doesn't wait for ClamAV to start.
- **Data-protection keys are stored in PostgreSQL** (`DataProtection:Store=Database`). The instance's disk is replaced on every deploy, so file-based keys would sign everyone out and make the saved AI key unreadable. The keys are encrypted at rest with an X.509 certificate held in Secret Manager.
- **Supabase PostgreSQL** is reached through the IPv4 session pooler, with TLS required. **Cloudflare R2** is used through the existing S3 client, with a configurable signing region (`auto`) and SDK checksums sent only when the S3 API requires them. KuTax keeps verifying SHA-256 itself.
- **Production validation gains a managed-services mode** (`Production:ManagedServices`). It requires TLS to the database, HTTPS storage, the scanner on localhost and certificate-encrypted keys. It is the alternative to the private-network mode, not a way to skip the checks.

## Consequences

- Cost follows use: Supabase Pro is the main fixed cost, and compute is billed while instances are up.
- The first upload after idle waits 10–40 seconds for its scan. Images must be rebuilt at least weekly to keep built-in signatures fresh.
- Losing the key certificate signs everyone out and requires the AI key to be entered again.
- Not yet run on real Cloud Run, Supabase or R2 accounts: see `docs/operations/cloud-run.md` for what was verified locally.
