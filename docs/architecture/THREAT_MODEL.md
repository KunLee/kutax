# KuTax threat model

Reviewed: 10 October 2026, for the M6 personal-trial gate. Scope is the single-image deployment from [KUTAX_HANDOFF.md](KUTAX_HANDOFF.md): Angular SPA + ASP.NET Core API in one container, PostgreSQL, private RustFS and a private ClamAV service. Revisit this document when a trust boundary, external provider or deployment host changes.

## Assets

| Asset | Where it lives | Why it matters |
|---|---|---|
| Receipt originals and evidence | RustFS `kutax-private`, opaque server-generated keys | Sensitive personal-financial records |
| Extracted facts, allocations, claims, audit history | PostgreSQL | Determines claimed amounts; must stay attributable |
| Annual snapshots and export packages | PostgreSQL (hashed JSON) + RustFS (PDF/XLSX/ZIP) | What the user transfers into myTax |
| Identity accounts, cookies, data-protection keys | PostgreSQL + `/app/keys` volume | Session forgery if keys leak |
| Service credentials | Protected env / user secrets, never Git | Direct database/storage access |
| Backups | Operator-chosen directory (`artifacts/recovery` locally) | Contain everything above in one place |

## Trust boundaries

1. **Browser → app**: the only public listener. Everything arriving here is untrusted, including uploaded file bytes and filenames.
2. **App → PostgreSQL / RustFS / ClamAV**: private service network. Production validation refuses to start unless `Production:PrivateServiceNetwork` is declared.
3. **App → optional AI provider**: leaves the deployment. Off by default; needs operator configuration *and* per-user revocable consent.
4. **Receipt content → extraction → review**: receipt text and model output are untrusted data, never instructions.
5. **Operator → backup media**: backups leave the running services and must be protected separately.

## Threats and controls

| Threat | Control | Evidence |
|---|---|---|
| Another user enumerates, downloads or edits someone's receipts, claims or exports | Every query scoped by owner ID; foreign IDs return 404; objects are only served through authenticated endpoints | Cross-account API tests; real-service nested import and export download ownership tests |
| Anonymous or public object access | Private bucket, no public policy, keys never sent to the browser | Integration test asserts anonymous object read and bucket listing are denied |
| CSRF on cookie-authenticated writes | Antiforgery token required on all mutating endpoints; `SameSite=Strict` cookies | CSRF-omission API test and container smoke script |
| Password guessing | Identity lockout (5 attempts / 15 min), `login` rate-limit policy, no public registration | Lockout API test |
| Stolen or stale session after password/role change | Security-stamp revocation on password reset, disable and role grant | User-lifecycle integration test through the running image |
| Malicious upload (malware, polyglot, oversize) | 20 MiB bounded bodies, PDF/JPEG/PNG signature checks, ClamAV INSTREAM scan; documents stay unavailable until clean | Scanning integration tests (EICAR quarantine, scanner outage) |
| Scanner outage used to bypass scanning | Fail-closed: unscanned/quarantined documents are excluded from preview, extraction, claims and exports | Outage test proves the scanner never reports clean when unreachable; the exclusion itself is enforced by status checks in the receipt, review, claim and export endpoints and workers (no dedicated end-to-end outage test yet) |
| Active content in previews or exports | Strict CSP on the app and on served documents; `nosniff`; PDF.js renders without scripts or external actions | Container browser checks found and fixed a CSP conflict |
| Prompt injection inside receipt text | Model output must pass strict JSON Schema plus semantic validation; AI only *suggests*, a human confirms facts, and deterministic FY rules calculate | `Extraction_rejects_invented_invalid_and_untraceable_fields`; every tenth evaluation fixture contains injected instructions |
| AI provider retains or trains on receipts | Opt-in only, operator records provider/region/retention notice, user consent is revocable, `store:false`; raw prompts/responses never logged | ADR 0003. `store:false` is not a zero-retention guarantee |
| Unsupported or invented tax treatment | Sourced FY-versioned rule catalogue, separate administrator rule review, owner approval, unsupported scenarios excluded | M4 golden tests; tax-rules docs |
| Tampering with historical reports | Immutable snapshot/claim triggers in PostgreSQL; SHA-256 snapshot hash; evidence ZIP manifest with per-file hashes | M5 integration tests; recovery verification re-checks hashes |
| Sensitive data in logs or errors | Typed problem responses, no request-body or provider-payload logging, opaque object keys instead of filenames | Code review; continue to spot-check logs during the pilot |
| Acting on another person's records without permission | `X-KuTax-Act-As` is honoured only for an Active, accepted delegation from an enabled owner to the signed-in user, checked before any endpoint runs; anything else is 403 `DelegationEnded`. In-app acceptance only for accounts older than the invitation; otherwise the single-use emailed link (hashed at rest, 7-day expiry). Every change is recorded with the actual actor | `SharingTests` (refused before acceptance, for strangers and after ending; attribution; link single use; new accounts need the link) |
| Insecure production configuration | Startup refuses HTTP origin, wildcard hosts, relative key path, weak credentials, missing scanner or undeclared private network | `ProductionConfigurationTests` |
| Data loss or corrupt restore | Consistent backup with app writers stopped; restore into fresh isolated volumes; `--verify-recovery` checks every object hash, snapshot reconciliation and record counts and must match the source exactly | `scripts/Test-Recovery.ps1` drill passed 10 October 2026 |

| Mass or abusive self-registration | `Registration:Enabled` defaults off; per-address limit of 10 attempts per hour; CSRF; strong password policy; new accounts are members only | `RegistrationTests` |
| Exchange-rate download tampering or abuse | Fixed HTTPS URL, no redirects, 20 s timeout, 5 MB cap and strict CSV parsing; only public rate data is fetched; RBA estimates are recomputed server-side on save | `ForeignCurrencyTests` |
| AI provider key disclosure | Key encrypted at rest with the data-protection keys; write-only API returning only the last 4 characters; administrator-only endpoints with CSRF; endpoint not configurable from the UI | `AiSettingsTests` |
| Receipt data sent to Groq | Opt-in configuration plus per-user consent tied to provider, models, region and retention notice; Zero Data Retention required by the setup notes; no tools; strict schema; untrusted-data markers; nothing sent for unreadable inputs | `GroqExtractorTests`; ADR 0006 |
| AI category suggestions mislead or leak | Suggestions only pre-fill a choice; deterministic reviewed rules calculate and the owner approves each batch with explicit declarations. Back-filling older receipts sends merchant names only (no amounts, dates, images or notes), under the same consent, wrapped as untrusted data; unknown categories are dropped | `QuickReviewTests`; ADR 0009 |
| Bulk approval bypasses checks | Quick review re-derives eligibility and versions server-side inside one transaction, rejects any item not currently eligible or owned, and approves only rule-assessed candidates with per-claim audit (`via: QuickReview`) | `QuickReviewTests` |
| Wrong or manipulated tax rules | Rules change only by administrator activation of a validated pack: schema/range checks, official ATO sources only, sandboxed decimal formula language (no code execution, size/depth caps), mandatory worked examples, verbatim-quote verification against downloaded sources for AI drafts, two independent drafts compared; immutable versions, filtered unique active index, audit events | `RulePackTests`; ADR 0010 |
| Prompt injection via ATO pages | Source text is wrapped as untrusted data; the model can only fill a fixed schema; every figure must be quoted from the source and verified by code; nothing activates without an administrator | `RulePackTests`; ADR 0010 |
| Account directory exposure through sharing suggestions | `GET /api/sharing/people` suggests people you've shared with first, then other enabled accounts only after two typed characters, at most six, never yourself; returns name and email only | `SharingTests` |
| Unsafe profile photos | The app shrinks photos to a 256-pixel JPEG; the server accepts only JPEG/PNG/WebP recognised by their first bytes, at most 200 KB, stored apart from the account and served only to its owner | `UserManagementTests` |
| Administrator misuse of other accounts | Administrators can't change another person's name or email; promotion and demotion sign the person out, never apply to yourself, and keep at least one enabled administrator; notes are visible to administrators only | `UserManagementTests` |
| Spoofed client address or scheme behind Cloud Run | Forwarded headers are trusted only in managed mode, where the container is reachable solely through Google's front end, and only for the last hop (`ForwardLimit` 1). A client-supplied `X-Forwarded-For` can't replace the address Google appends. | Production verification (deploy health check) |

## Open risks (accepted for the personal trial, not for wider use)

- **Any signed-in person can discover other account addresses by typing in the sharing box.** Fine for a household or small group; for wider use, restrict suggestions to people you've shared with.
- **Backups are not encrypted by KuTax.** The drill restricts directory permissions only. Store backups on encrypted media or encrypt them before they leave the host.
- **Encryption at rest is a host responsibility.** PostgreSQL and RustFS volumes rely on disk/volume encryption from the host.
- **Local RustFS credentials are the RustFS administrator.** Production needs a scoped access key limited to the KuTax bucket.
- **TLS is terminated by the host's reverse proxy.** KuTax enforces HTTPS origin and trusted-proxy configuration but cannot verify the proxy's certificate setup.
- **No MFA or email recovery.** Administrator-assisted reset only; acceptable for a single household, not for multi-tenant use.
- **AI extraction accuracy is unmeasured.** The 100-receipt synthetic set exists, but the ≥95% evaluation requires an operator-configured provider and consent. Manual entry remains the default.
- **Self-registration has no email verification or CAPTCHA**, and duplicate-email responses allow slow enumeration. Acceptable for a local or invited deployment, not for the open internet.
- **Groq's vision model is a preview model** and may be withdrawn. Groq may store data in the US if Zero Data Retention is not enabled.
- **Hosted CI has not run.** All evidence so far is local.
