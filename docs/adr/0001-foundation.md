# ADR 0001 — M0 foundation

Date: 10 October 2026. Status: accepted for the personal-use foundation; the local Podman container gate has passed. Account provisioning/recovery decisions below are extended by [ADR 0002](0002-user-management.md).

Version-policy update: the owner requires latest stable releases. Consult [the maintained version baseline](../architecture/DEPENDENCIES.md) for current pins and compatibility exceptions. Test projects now use xUnit v3 with Microsoft Testing Platform, matching the stable .NET 10 test runner; the former VSTest-specific collector/adapter packages are replaced by the supported MTP packages.

## Context and decision

The repository contained a licence, Visual Studio ignore file, placeholder README and one untracked architecture handoff. There was no application, earlier ADR, migration or CI to preserve or reconcile. `docs/architecture/KUTAX_HANDOFF.md` remains the canonical specification; the suggested root `HANDOFF.md` is not duplicated.

Use the locked architecture: an Angular 22 standalone SPA and ASP.NET Core 10 host, with Core and Infrastructure projects. Keep same-origin relative `/api` URLs, PostgreSQL/EF Core and private RustFS storage. Core currently contains only financial-year record organisation; no eligibility or deductible-amount rules are implemented. Fifteen recent financial years are offered in the shell, and an existing older saved year remains selectable.

Use framework ASP.NET Core Identity with a server-side password hash in PostgreSQL and HttpOnly session cookies. All API writes, including login, validate an antiforgery token. Angular uses its standard `XSRF-TOKEN`/`X-XSRF-TOKEN` convention and refreshes the token after login/logout. Production cookies require HTTPS. Authenticated account endpoints compare the route owner ID against the signed-in user before querying or updating. Login has Identity lockout and a bounded global limiter suitable for this single-user slice.

Provision only a synthetic development account through an explicit, development-only seed command. Public registration, email delivery, password recovery and real production owner provisioning are outside this foundation. No development password is committed. The setup helper uses cryptographic randomness, an ignored Compose env file and .NET user secrets. User secrets are a development convenience, not an encrypted production secret store.

Apply migrations and initialise the bucket through explicit commands. A web process should not silently mutate schemas or create accounts. New S3 buckets use private defaults; an integration contract checks anonymous access is rejected. The S3 SDK and credentials stay in Infrastructure. M0 implements connectivity only; `IDocumentStorage`, object ownership and receipt lifecycle arrive in M1.

Pin RustFS to `1.0.1` plus the registry manifest digest resolved on 10 October 2026. This verifies image identity and availability, not runtime compatibility; the real contract test is still required. PostgreSQL 18 uses a volume at `/var/lib/postgresql`, matching that image's version-specific data layout. Local service ports bind only to loopback. Named volumes persist database/object data and the app's cookie-protection keys.

Build Angular and .NET in separate Docker stages, copy only published .NET files and static assets into a non-root ASP.NET runtime image. The optional local Compose app profile enables Development for HTTP localhost use. Production requires TLS, host allowlisting, protected key storage and operational review; do not expose the local profile publicly.

## Validation strategy and consequences

TLS-terminating proxies must forward the original scheme. Forwarded-header processing trusts framework loopback defaults plus explicit `ReverseProxy:KnownProxies` IP addresses; it does not trust arbitrary remote sources. Production antiforgery token issuance rejects plain HTTP, and session cookies require HTTPS. Configure the real proxy address and keep the app listener private.

Fast API tests use isolated SQLite with the real Identity and antiforgery pipeline. Separate opt-in tests run actual PostgreSQL migrations, signed RustFS object roundtrips and anonymous-access rejection. Skipped tests are reported as skipped, never as evidence of compatibility. CI runs both service tests and the single-image smoke gate. Keep dependency lock files and disallow preview SDK selection.

The foundation has no receipt processing, tax calculations, AI calls, confirmed myTax mappings, exports or lodgment. Those features require their own reviewed milestones. Report libraries, AI providers and tax-category selection remain open and do not block M0.

## Primary references consulted

- [Angular version compatibility](https://angular.dev/reference/versions) and [release policy](https://angular.dev/reference/releases).
- [ASP.NET Core Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) and [development user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).
- [RustFS Docker documentation](https://github.com/rustfs/docs.rustfs.com/blob/main/content/en/installation/container/docker.md), [official Compose example](https://github.com/rustfs/rustfs/blob/main/docker-compose.yml) and [release 1.0.1](https://github.com/rustfs/rustfs/releases/tag/1.0.1).
