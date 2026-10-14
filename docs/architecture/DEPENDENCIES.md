# Stable dependency baseline

Verified: 10 October 2026. The owner requires the latest stable releases across the solution. Check official release metadata and package registries when adding dependencies and before each milestone. Exclude previews, release candidates, beta/alpha versions and nightly builds. Pin the reviewed release and refresh its lock file; do not use floating `latest` image tags. Pinning makes today's stable selection reproducible, while the next review identifies subsequent releases.

| Component | Selected stable version |
|---|---|
| .NET SDK | 10.0.402 |
| ASP.NET Core, EF Core, Identity, EF CLI, Microsoft.Extensions | 10.0.13 |
| Angular framework, CLI and build tools | 22.2.2 |
| Node.js | 26.11.1, current stable release |
| npm | 12.2.0 |
| PostgreSQL | 18.6 |
| RustFS | 1.0.1, pinned registry digest retained |
| Npgsql EF Core provider | 10.0.3 |
| AWS S3 SDK | 4.0.104.2 |
| RxJS / tslib | 7.8.2 / 2.8.1 |
| jsdom / Prettier / Vitest | 30.1.2 / 3.9.9 / 5.0.3 |
| TypeScript | **6.0.3, compatibility exception** |
| xUnit framework (`xunit.v3.mtp-v2` package) | 4.0.2 |
| Microsoft.Testing.Platform / coverlet.MTP | 2.5.1 / 10.1.0 |
| GitHub checkout / setup-dotnet / setup-node | 7.0.1 / 6.0.0 / 7.1.0 |

## Compatibility exception

The latest standalone TypeScript release is **7.0.2**, but Angular compiler-cli **22.2.2** declares a TypeScript peer dependency of **`>=6.0 <6.1`**. Use **6.0.3**, the newest stable supported release. Do not bypass peer checks or disable Angular's compiler-version check. Revisit this exception when a stable Angular release supports TypeScript 7.

Node 26 is a stable Current release, and Angular 22's supported Node range includes 26. Selecting only older LTS versions would not meet the owner's latest-stable preference. .NET 11 and PostgreSQL 19 are still prereleases at this review and are excluded. Framework-owned transitive dependencies remain governed by their supported constraints and the reviewed lock files; do not blindly override them to incompatible versions.

## Verification sources

Tests use the current Microsoft Testing Platform integration (`xunit.v3.mtp-v2` and `coverlet.MTP`), with the .NET 10 runner selected in `global.json`. Commands use `dotnet test --solution` / `--project`. npm 12's explicit install-script allowlist is pinned to the reviewed native Angular build dependencies in `frontend/package.json`; fresh `npm ci` uses that same list locally, in CI and in the image build.

- [.NET stable downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [machine-readable release index](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json).
- [Angular compatibility](https://angular.dev/reference/versions), [compiler-cli peer metadata](https://registry.npmjs.org/@angular%2fcompiler-cli/22.2.2) and [TypeScript registry](https://registry.npmjs.org/typescript).
- [Node 26.11.1 release](https://nodejs.org/en/blog/release/v26.11.1) and [Node release index](https://nodejs.org/dist/index.json).
- [PostgreSQL supported versions](https://www.postgresql.org/support/versioning/) and [RustFS releases](https://github.com/rustfs/rustfs/releases).
- [npm registry](https://registry.npmjs.org/), [NuGet registry](https://api.nuget.org/v3/index.json) and [xUnit migration instructions](https://xunit.net/docs/getting-started/v3/migration).
- [GitHub checkout releases](https://github.com/actions/checkout/releases), [setup-dotnet releases](https://github.com/actions/setup-dotnet/releases) and [setup-node releases](https://github.com/actions/setup-node/releases).

Container tag availability is checked against the official registries. The local M0 runtime gate has now passed using Podman Compose and the built image, including PostgreSQL migrations and RustFS contracts. The subsequent user-management extension reuses these versions without new dependencies and has also passed real-container checks. Hosted CI execution remains unverified.

Receipt review adds PDF.js (pdfjs-dist) **6.4.299**, the npm latest stable release verified 10 October 2026. Apache-2.0 licensed. Dynamic import keeps it outside the initial application bundle; its worker/fonts/CMaps/WASM assets are served locally. PDF page rendering does not invoke document scripts or external actions. Source: https://registry.npmjs.org/pdfjs-dist and https://mozilla.github.io/pdf.js/examples/.

Annual exports use [PDFsharp-MigraDoc 6.2.4](https://www.nuget.org/packages/PDFsharp-MigraDoc) and [ClosedXML 0.105.1](https://www.nuget.org/packages/ClosedXML), latest stable packages verified 10 October 2026. Both are MIT licensed. PDFsharp 7 preview is excluded. These packages support the .NET 10 host; refreshed backend lock files include their supported transitive dependencies.

Reports embed [Noto Sans v2.015](https://github.com/notofonts/latin-greek-cyrillic/releases/tag/NotoSans-v2.015), the latest stable Noto Sans release in the upstream family repository, under SIL Open Font License 1.1. The unchanged licence is bundled beside regular and bold fonts. Regular SHA-256: `478C558EA716033CD60C03438F628DFA75694DCF6B5F6D505A2F05FD2B4F3823`; bold: `1DF075A380FC7CB898ACF64C1F7B3B4DD780DE3CAA860178BF929DE35817A913`. No platform font dependency is required.

Chinese tax-pack PDFs embed Noto Sans SC from the [google/fonts](https://github.com/google/fonts/tree/main/ofl/notosanssc) variable font (SIL Open Font License 1.1, licence bundled as `OFL-NotoSansSC.txt`). Regular and bold static instances were cut from the variable font and subset to the CJK, Latin and punctuation the reports need. Their name tables, weight class and style bits were rewritten because PDFsharp resolves faces by family and style name. Regular SHA-256: `2649C7400864CF12E0045DA540D3230C714A0C6BFCE13C31209BA3481CBBE0BB`; bold: `BF0CAD62EB0391763A6D80A9C2C2867165D16C9B28FF224256426D964DDF0740`. The interface needs no font change: browsers use the system's Chinese font.

Malware scanning uses [ClamAV 1.5.4](https://github.com/Cisco-Talos/clamav/releases), the latest stable release verified 10 October 2026, as a separate GPL-2.0 service communicating through the documented clamd INSTREAM protocol. The official `clamav/clamav:1.5.4` Linux AMD64 manifest is pinned to `sha256:7659dcb0db47941d3cf8336af84bbb63c7e70b76fc00601774358412b42ed186`. Signature data is updated by freshclam and persisted separately; it is intentionally not frozen to an obsolete database. Clamd is private except for a loopback-only local testing port. A scanner outage never authorises preview/extraction/claims/exports.

The mobile-first UI uses [@fontsource/manrope 5.3.0](https://www.npmjs.com/package/@fontsource/manrope) and [@fontsource/dm-mono 5.3.0](https://www.npmjs.com/package/@fontsource/dm-mono), the latest stable releases verified 10 October 2026, both SIL Open Font License 1.1. These are the typefaces in the Figma design (`docs/figma`). Only the Latin subsets at the weights in use are bundled. Fonts are served from the application origin because the CSP (`default-src 'self'`) blocks external font hosts such as Google Fonts.

Foreign-currency estimates use the [RBA F11.1 daily exchange-rate table](https://www.rba.gov.au/statistics/tables/csv/f11.1-data.csv). It is public data with no package dependency, downloaded server-side and stored locally ([ADR 0008](../adr/0008-foreign-currency-and-year-guardrail.md)).

Groq extraction reads PDF text with [PdfPig 0.1.16](https://www.nuget.org/packages/PdfPig), the latest stable release, verified 10 October 2026. It is Apache-2.0 licensed, fully managed code with no native binaries, and has no dependencies on .NET 8 or later. Lock files are refreshed. See [ADR 0006](../adr/0006-groq-extraction.md).

Managed hosting (ADR 0012) adds [Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 10.0.13](https://www.nuget.org/packages/Microsoft.AspNetCore.DataProtection.EntityFrameworkCore), the latest stable release (verified 14 October 2026), which matches the ASP.NET Core 10.0.13 runtime. It keeps data-protection keys in PostgreSQL. The Cloud Run ClamAV sidecar builds on the same pinned `clamav/clamav:1.5.4` digest and adds signatures at build time. `infra/cloudrun/cloudbuild.yaml` uses Google's `gcr.io/cloud-builders/docker` builder, which Cloud Build maintains.

Sharing invitations (ADR 0013) are sent with [MailKit 4.18.1](https://www.nuget.org/packages/MailKit), the latest stable release (published 27 September 2026, verified 13 October 2026), through any SMTP provider. Local development adds [Mailpit v1.31.5](https://github.com/axllent/mailpit/releases) (`axllent/mailpit:v1.31.5`, pinned by digest), the latest stable release, which catches every email at http://localhost:8025 and sends nothing out.

**.NET 10.0.13 servicing (14 October 2026).** NuGet audit flagged `System.Security.Cryptography.Pkcs` and `System.Security.Cryptography.Xml` 10.0.12 and earlier (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx, GHSA-7hqp-7c5m-4vq6). All Microsoft packages, the EF CLI, the runtime image (`aspnet:10.0.13`) and the SDK (10.0.402, also in the build image) were moved to the fixed servicing release. MimeKit 4.18.1, a dependency of MailKit 4.18.1 (still the latest), asks for Pkcs 10.0.0. The infrastructure project therefore references `System.Security.Cryptography.Pkcs` 10.0.13 directly; remove that pin once MimeKit requires a fixed version. `dotnet list package --vulnerable --include-transitive` reports nothing.

**Continuous deployment (15 October 2026).** `.github/workflows/deploy.yml` uses [google-github-actions/auth v3.0.0](https://github.com/google-github-actions/auth/releases) and [google-github-actions/setup-gcloud v3.0.1](https://github.com/google-github-actions/setup-gcloud/releases), the latest stable releases, verified 15 October 2026 (alongside `actions/checkout` v7.0.1 already used by CI). Authentication is Workload Identity Federation, with no stored service-account key.
