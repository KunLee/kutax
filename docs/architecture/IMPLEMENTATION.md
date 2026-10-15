# KuTax implementation status

Updated: 11 October 2026. Scope: M0 plus the owner's subsequent request for administrator account management and personal settings, following `KUTAX_HANDOFF.md`.

The owner's subsequent latest-stable requirement is recorded in [DEPENDENCIES.md](DEPENDENCIES.md) and AGENTS.md. The framework/tool/image baseline has been reviewed and updated, with one explicit Angular/TypeScript compatibility exception. Older illustrative version numbers are superseded by this maintained baseline.

## Assessment

The starting repository had no implementation, migrations, CI or older plans/ADRs. The architecture handoff was untracked and has been preserved. There were no conflicting decisions and no blocking product questions. The placeholder README has been replaced, and AGENTS.md now points to the existing canonical handoff path.

## Tickets

- [x] M0.1: inspect repository; add working guidance and ADR; retain locked architecture.
- [x] M0.2: scaffold Angular and API/Core/Infrastructure; same-origin dev proxy; responsive sign-in and workspace; financial-year selection.
- [x] M0.3: Identity cookies, CSRF on writes, login lockout/rate limiting, account ownership enforcement; negative API tests.
- [x] M0.4: PostgreSQL EF migration and explicit synthetic development seed; pinned private RustFS setup/connectivity; persistent loopback-only Compose configuration.
- [x] M0.5: one production app Dockerfile, dependency lock files, CI pipeline, quick start and random-credential helper.
- [x] M0.6: Compose startup with fresh local volumes, PostgreSQL migrations and RustFS contracts; build/start the application image and verify the authenticated UI against those services. **Local M0 acceptance gate passed.** A separate fresh-clone/hosted CI run is still unverified.
- [x] M0.7 (owner-requested extension): administrator account search/pagination, creation, name/email editing, disabling/enabling, unlock and temporary-password reset; personal name/password settings; explicit administrator provisioning; migration and session-revocation tests. Details and deliberate limits are recorded in [ADR 0002](../adr/0002-user-management.md).

## Evidence and limits

- Backend: 10 API/domain tests passed using isolated SQLite. Covers July/June and leap-year boundaries, invalid years, unauthenticated access, real Identity cookie login/logout, invalid credentials, account lockout, cross-account reads/writes, CSRF omission, identity-bound token renewal and production HTTPS/HttpOnly cookie behaviour.
- Frontend: 5 tests passed. Production Angular build passed without warnings. Tests include saved year display/selection, sign-in failure handling and the token-refresh sequence.
- EF: initial PostgreSQL migration generated; idempotent SQL generated; model drift check passed. Real PostgreSQL 18.6 migration applied successfully and a second application made no changes.
- Development setup helper executed successfully; ignored credentials generated and .NET user secrets configured. No credentials printed or committed.
- Desktop (1440px) and mobile (390px) sign-in/workspace layouts visually inspected in Playwright. Workspace rendering used explicit synthetic API fixtures; it is not evidence of service integration. Date-only financial-year boundaries display 1 July–30 June in Perth without UTC shifts.
- Locally published Angular + .NET assets served successfully from one process, including deep-link SPA fallback, API health, unknown-API 404 and anonymous-session 401. Container execution remains a separate check.
- Both real-service integration tests passed with `KUTAX_INTEGRATION=1` against Podman-hosted PostgreSQL 18.6 and RustFS 1.0.1: repeatable migrations, synthetic account/login/readiness, signed object upload/read/delete, anonymous object and bucket-list denial. Tests still explicitly skip when the opt-in flag is absent.
- Stable-version update: clean npm 12.2.0 install, Angular 22.2.2 production build and all 5 frontend tests passed on Node 26.11.1. All 10 backend tests passed with SDK 10.0.401 and the xUnit v3/Microsoft Testing Platform runner; 2 real-service tests skipped explicitly. Isolated verified toolchains were used without replacing the machine's installed SDK/Node versions. npm's direct-dependency audit reports only the documented TypeScript exception.
- Podman was initially overlooked when Docker CLI was unavailable. The running Podman 5.4.1 WSL machine and its Compose v2.34.0 provider were subsequently used to start fresh KuTax volumes, build the complete multi-stage image, run explicit setup/seeding and serve Angular/API on port 5080. Other projects' containers and volumes were left untouched.
- Running-image HTTP smoke checks passed: readiness, SPA root/deep link, API 404, anonymous 401, CSRF omission 400, authenticated login/logout, account ownership 404 and saved year across login. `scripts/Test-Application.ps1` repeats these checks, restores the synthetic account's year and now runs in CI.
- Browser sign-in and authenticated workspace used the actual container/API/database/storage, with no API fixtures. The check found and fixed a production CSP conflict with Angular's inline critical-CSS loader. Podman also exposed argument splitting in the RustFS health check; using Compose `CMD-SHELL` fixed it. Both service health checks now pass. Hosted GitHub Actions execution remains unverified.
- Final verification: all 12 backend tests passed with no skips. Desktop/mobile browser checks confirmed the corrected styles, saved-year reload and logout. After restarting all three containers, the authenticated browser session, selected year and storage readiness survived. Synthetic account year restored; the local stack remains running at `http://localhost:5080`.

User-management verification: all **19 backend tests passed with no skips** (16 API/domain plus 3 real-service tests), and all **12 frontend tests passed**. The image built successfully, the additive migration applied to the existing Podman PostgreSQL volume, and the EF model-drift check passed. The user lifecycle ran through `http://localhost:5080` using actual container cookies and PostgreSQL, including first-password change, saved profile/year, disable/enable, reset and session revocation; unique synthetic fixture accounts were removed. Desktop/mobile admin and personal-settings browser checks used the actual stack, including a name save/reload/restore. The existing demo account is now an administrator; its password and selected year were retained. Hosted CI remains unverified.

The published-image `--bootstrap-admin` command was also exercised with ephemeral protected environment settings. Its new account signed in through the running image with administrator access and a required initial password change. That unique operator-test fixture was removed afterward.

## MVP implementation in progress

The owner has authorised all MVP milestones. M1 now has receipt/import business tables, a generated migration, streaming validation, checksums, private RustFS writes and reads, owner-scoped metadata, idempotent import results and an Angular inbox with recursive folder/file drop, file/folder/camera pickers, preview, two concurrent uploads, progress, cancellation and retry. The real PostgreSQL/RustFS nested import test passed, including byte-for-byte download, duplicate counts and cross-account denial. The built Podman application passed a browser folder import; screenshot: ignored `output/playwright/m1-inbox.png`.

M2 now has processing-job, extraction-attempt, transaction, audit and consent models with a generated migration; a leased PostgreSQL worker; a manual default extractor and configurable opt-in Responses adapter; schema/semantic validation; human correction with optimistic concurrency; and side-by-side review UI. **21 backend tests passed with no skips**, including the real PostgreSQL/RustFS expired-lease recovery and correction-preservation checks. **12 frontend tests passed**. The actual Podman browser flow confirmed receipt facts and rendered a valid PDF using locally served PDF.js 6.4.299; ignored screenshots `output/playwright/m2-review.png` and `m2-valid-pdf-preview.png`. The external provider has not been called.

M3 allocation models/API/editor and migration are applied and tested. A real receipt was split into two expenses in the browser (`output/playwright/m3-allocations.png`). Real-service tests cover allocation bounds, stale writes, resource ownership and actor-attributed audit history. Additional evidence links and append-only claim decisions are being added with M4; that gate remains open.

The earlier command-runner interruption recovered. PostgreSQL, RustFS and the latest M1–M3 application image are running on the existing KuTax volumes at `http://localhost:5080`. No unrelated containers or volumes were changed.

M4 is in progress: limited sourced FY 2024–25 / 2025–26 periodic union and professional-membership coverage, separate administrator rule review and owner claim approval, deterministic decimal arithmetic, supplementary evidence links and preserved historical approvals. M4 tests/migration/UI acceptance remain pending. Annual exports and M6 operational delivery checks remain outstanding. This is not yet delivery-ready.

## Deferred deliberately

Tax rules/claim approval, annual snapshots and exports, malware handling, backup/restore and operational delivery checks remain outstanding. Import, processing, audit, consent and allocation progress is described above; unfinished acceptance checks are explicit. No real receipts should be used yet. Email-based recovery/verification and MFA remain outside this initial personal MVP. Explicit operator provisioning and administrator-assisted member password reset are implemented.


M4 verification: limited FY rules, pure decimal golden cases, explicit administrator rule review and owner approval, supplementary evidence links and historical decision preservation are implemented. All 28 backend tests passed with no skips before the subsequent scanner addition. Browser rule-review and two-claim approval passed using synthetic records (`output/playwright/m4-claims.png`). Rule reviews recorded by the automated pilot are explicitly labelled synthetic; they are not independent operator/professional sign-off for real claims. The allocation upgrade default issue found by the pilot is corrected using the latest saved allocation IDs from the existing audit history.

M5 verification: immutable snapshot and claim-update database triggers; preview hashing; durable leased export queue; PDF summary and entry guide; six-sheet XLSX; evidence ZIP with SHA-256 manifest; download ownership; entry progress and history. Four real-service integration tests passed after adding cross-format reconciliation and expired export-lease recovery, including historical snapshot preservation after evidence changes. Both two-page PDFs were rendered and their first pages visually inspected; remaining page/readability and browser downloads need final inspection. The actual image browser flow prepared a synthetic $110 package, copied the whole-dollar value and saved Checked entry progress; desktop/mobile screenshots `output/playwright/m5-exports.png` and `m5-mobile.png`. M5 broad delivery regression remains to be repeated after M6 changes.

M6 in progress: ClamAV 1.5.4 is running healthy with an updated signature database. Document scan-state migration, fail-closed access, scanner/quarantine tests, original integrity checks, repeatable recovery CLI and isolated backup/restore script are implemented.

M6 verification (10 October 2026, later session): **31 backend tests passed** (5 integration tests skipped without the opt-in flag). The same build then ran **all 5 real-service integration tests with no skips** against Podman PostgreSQL, RustFS and ClamAV, with the app stopped for worker isolation. **12 frontend tests passed** on Node 26.11.1.

- **Restore drill passed.** Earlier drill attempts failed during restore because Podman on Windows cannot `podman cp` host files into containers. `Test-Recovery.ps1` now reads the archives and dump through a read-only bind mount. Its readiness probe now uses TCP, because the PostgreSQL image's temporary init server answers on the Unix socket before the real server starts. The drill backed up and restored into fresh isolated volumes. The restored `--verify-recovery` result matched the source exactly: 6 objects / 136,660 bytes hash-verified, 2 users, 2 documents, 2 transactions, 4 allocations, 2 claims and 1 reconciled snapshot. Orphaned volumes from the failed attempt were removed. The app was restarted afterwards.
- Production configuration validation runs at Production startup and is covered by `ProductionConfigurationTests`: HTTPS origin, explicit hosts, absolute key path, credential strength, scanner endpoint and private service network. Actual certificate/proxy setup remains a host responsibility.
- [THREAT_MODEL.md](THREAT_MODEL.md) records assets, trust boundaries, controls with test evidence and accepted open risks. KuTax does not encrypt backups. Encryption at rest is left to the host. Local RustFS uses administrator credentials. There is no MFA. The scanner-outage exclusion has no dedicated end-to-end test.
- `--generate-evaluation-fixtures` produced 100 labelled synthetic PDFs offline. **The ≥95% AI accuracy evaluation has not run**: it needs an operator-configured provider and explicit `Evaluation:ExternalAiConsent`. README now documents the drill and both evaluation commands.

**Mobile-first redesign (10 October 2026).** The UI now follows the Figma design in `docs/figma`. It uses routed Inbox, Review, Summary, Settings and Users screens, a phone bottom tab bar and self-hosted fonts. See [ADR 0004](../adr/0004-mobile-first-ui.md). The document list API gained review states, counts and a `state=review` filter. Every figure on screen comes from the API.

- **Tests.** 31 backend tests passed. All **6 real-service integration tests passed with no skips**, including the new PostgreSQL document-list test. **14 frontend tests passed**; new specs cover API-only Inbox figures and the Summary deduction mix. The production build has no warnings, and the initial transfer size is now 108 kB.
- **Browser and accessibility checks.** Run with Playwright (system Chrome) against the rebuilt application image with its CSP enforced. They covered sign-in, Inbox, Review queue, receipt detail, Summary, Settings, Users and the account menu at 390px, 360px and 1440px. **No horizontal overflow, and axe found zero WCAG 2.1 A/AA violations on all 21 screen/size combinations.** The only console errors were the expected anonymous-session 401s. The container smoke script also passed. Screenshots are in the ignored `output/playwright/redesign/` folder.
- **Bugs the checks caught and fixed.**
  - The header's `backdrop-filter` anchored the phone tab bar to the header.
  - The manual extractor was labelled as "Read by KuTax".
  - Admin user cards were cramped on phones.
- **Test accounts removed.** Two empty synthetic `list-…` accounts were left by failed cleanup in an early draft of the new integration test; they were removed and the test now clears tracked entities first. One older empty `scan-…` account from an earlier interrupted run was also removed.

A follow-up pass added 320px phone, 768px tablet and 844×390 landscape checks. It found and fixed tall PDF pages overflowing the preview and covering the receipt form on short screens; the canvas is now constrained itself. Landscape phones now get a static header and a compact tab bar. All six sizes passed against the rebuilt image: 42 screen/size combinations with no horizontal overflow and zero axe violations.

The automated app accessibility audit is done. A manual screen-reader pass and the report PDF readability review have not been done yet.

**Sign-up and Groq (10 October 2026).**

- **Self-registration ([ADR 0005](../adr/0005-self-registration.md)).** Covered by API tests for CSRF, validation, duplicate email, member-only access, the disabled switch and the per-address rate limit. A phone browser test against the rebuilt image registered a synthetic account and found zero axe violations; the account landed as a member with no admin link and was removed afterwards.
- **Groq extraction ([ADR 0006](../adr/0006-groq-extraction.md)).** Unit-tested with a fake HTTP handler: request shape, strict schema, no tools, PDF text path, manual fallbacks, rejected output and consent versioning. **Groq has not been called yet.** That needs the owner's key in `infra/.env`.
- **Owner account.** The owner's administrator account was provisioned with `--bootstrap-admin`. A password change is required at first sign-in.
- **Tests.** 38 API, 6 real-service integration and 16 frontend tests pass, plus the container smoke check.

**Product-readiness iteration (11 October 2026).**

- **AI settings in the app.** Administrators manage AI settings under Settings → AI provider, stored with the key encrypted ([ADR 0006](../adr/0006-groq-extraction.md)). The live Groq key check listed 11 models.
- **Groq rate limits.** Requests now honour `Retry-After`: the queue pauses, the receipt is requeued without losing an attempt, and an exhausted daily quota falls back to manual entry. Covered by a unit test and a real-PostgreSQL worker test.
- **Groq accuracy.** On 100 synthetic receipts: merchant 100%, amount 100%, date 86%. All date misses were day-first Australian dates; the instructions were fixed. **The re-run scored merchant 100%, date 100%, amount 100%, with 0 failures, meeting the ≥95% target.** Scope: machine-readable synthetic PDFs on the text-model path only. The photo path and real receipts are not yet measured.
- **Tax return history.** `/history` lists every year's tax pack: approved deductions, myTax amount, evidence and entry progress, with earlier versions folded away. `/history/:id` adds downloads with plain descriptions, myTax entry steps with copy and progress, approved claims with evidence links, items not claimed, acknowledged exceptions, rules applied and integrity details. New endpoint `GET /api/tax-exports`.
- **Help and checklist.** `/help` covers how it works, what is and isn't calculated, key dates and record-keeping, an FAQ and 8 official links verified on 11 October 2026; several guessed ATO URLs returned 404 and were replaced. The Inbox shows a four-step checklist driven by real data that hides when done.
- **Fixes.** Review and Inbox refresh themselves while receipts are being read; "All caught up" no longer appears while files are still processing. New accounts open on the year being lodged during July–October. Pluralisation fixed. The 17 MB PDF link is labelled.
- **Tests.**
  - 45 API, 7 real-service integration and 16 frontend tests pass.
  - 60 screen/size combinations (10 screens × 6 sizes) against the production image: no overflow and zero axe violations.
  - A brand-new user's full phone journey passed: sign-up → upload → review → confirm → split → approve → tax pack → History details → PDF download. Its synthetic accounts, rows and stored files were removed afterwards.
- **Known gap.** The FY 2025–26 rule reviews were recorded by the earlier automated pilot and labelled synthetic. Reviews cannot currently be withdrawn or superseded, so the owner cannot record their own sign-off for those versions.

**Owner feedback fixes (11 October 2026).**

- **All 58 receipts needed manual entry.** The owner's 58 uploads were all read by the manual extractor because AI was never enabled and no consent existed. The UI didn't say so.
  - An AI status banner now explains the state on Inbox, the review queue and manual receipts: off (admin link or "ask your administrator"), needs consent (link), or on.
  - Re-reading now fills untouched manual drafts and never replaces typed or confirmed details.
  - New `POST /api/documents/read-again` queues every unconfirmed, untouched receipt once AI is on and consented. It is the "Read waiting receipts with AI" button. Covered by a real-PostgreSQL worker test.
- **"Open original" showed an error page.** The original's `sandbox` CSP blocks Chrome's PDF viewer. Originals and evidence links now download (`?download=1`, attachment) and open in the device's own viewer. The strict CSP is unchanged.
- **The preview was too small.** A new document viewer for PDFs and photos adds page controls, 100–400% zoom with sharp re-rendering (bitmap capped for phone memory) and a full-screen mode that closes with Esc. Browser testing found and fixed two resize feedback loops: a scrollbar oscillation, and runaway width on phones.
- **Tests.** 45 API, 8 real-service integration and 16 frontend tests pass. 60 screen/size combinations show no overflow and zero axe violations. Viewer, download and banner were checked at phone and desktop sizes.

**Review speed, scans and sign-off (11 October 2026).**

- **"Not a work expense".** A new transaction status `Dismissed`, set through `PUT /api/transactions/{id}/dismiss` with the version and a restore option. It keeps the record, takes it out of the queue, AI re-reading and annual exceptions, and is refused while expenses exist. Review advances to the next receipt automatically. After confirming, the receipt offers "Next receipt", or says it was the last one. "Save draft" moved below the action bar. Before this change, every undated receipt appeared as an exception in every year.
- **Scanned PDFs.** PDFs with no text layer send up to 3 page images (≥300 px on each side; JPEG unchanged, otherwise PNG) to the vision model instead of falling back to manual entry.
- **Photo and scan accuracy.** Measured live through the real extractor on 40 synthetic receipts rendered in a browser: 20 skewed photos and 20 image-only scanned PDFs, using day-first Australian dates including ambiguous ones. Merchant, date and amount were each 100%, with 0 failures. This confirms Groq's preview vision model works with strict JSON Schema output. Real-world photos (lighting, creases, handwriting) remain unmeasured.
- **Rule sign-off.** Administrators can supersede an existing rule review. The earlier review is kept in the audit history and in frozen packs. The rule panel shows who reviewed, when and the note, with "Record my own review".
- **Tests.** 47 API, 9 real-service integration and 16 frontend tests pass. 60 screen/size combinations are clean. A phone browser run of the fast review flow passed: dismiss → auto-advance → confirm → "All caught up" → Not claimed → Restore.

**Review visibility, processing state and layout (11 October 2026).**

- **What the list shows.**
  - The document list now returns `aiRead`, and the review queue lists AI-read receipts first.
  - List chips: "AI read · check", "Type details", or a spinner with "Reading…".
  - A receipt whose job is queued or running is now `Processing` even if a draft exists. Before this, "Read again" showed nothing.
- **Processing indicators.**
  - A global "Reading N" pill appears in the header and polls while anything is processing.
  - The open receipt shows a "Reading this receipt…" banner and refreshes itself until done.
  - Receipts AI couldn't read now say so.
- **Layout.**
  - On desktop, opening a receipt hides the queue; "Show list" restores it.
  - The document takes 58% of the width and stays in view while you scroll the form; the page widens to 1640 px.
  - The desktop viewer grew from about 290 px to about 740 px wide.
  - Fixed along the way: a sticky rule that was overridden by a later `position: relative` but kept its `top` offset, and a full-screen height regression.
- **Owner's real use.** The owner enabled Groq and consented. Groq filled 54 of 58 receipts (6 already confirmed); 4 failed whole-payload validation. Since then:
  - Extraction results are sanitised per field: invalid fields are blanked with a note, verified fields are kept, and malformed payloads are still rejected.
  - Amounts are only filled when in AUD; foreign-currency receipts (such as USD invoices) leave the amount blank and show the original currency as a note under the field.
- **Tests.**
  - 48 API, 9 real-service integration and 16 frontend tests pass.
  - The re-read test now preserves the workspace's real AI settings; verified byte-identical before and after.
  - 60 screen/size combinations are clean.

**Switchable themes (11 October 2026).** The Kutax2 "retro toy-electronics" design is now the default theme and the original is "Classic" ([ADR 0007](../adr/0007-switchable-themes.md)).

- Switch in Settings → Appearance or from the account menu; the choice is remembered per device and applied before first paint.
- Verified: 17 frontend tests (including the theme service). In each theme, 60 screen/size combinations show no overflow and zero axe violations; axe found and drove five Retro contrast fixes.
- Fixed along the way:
  - a full-screen viewer regression: `align-self: start` from the sticky desktop rule stopped the fixed overlay stretching;
  - a fixed viewer height that pushed the full-screen controls off-screen.
  Full screen now fills the window in both themes with its controls visible.

**Foreign currency, year guardrail and viewer memory (11 October 2026)** ([ADR 0008](../adr/0008-foreign-currency-and-year-guardrail.md)).

- **Foreign currency.** Receipts can carry their original currency and amount. AUD comes from the person's statement (recommended) or an RBA daily-rate estimate that the server recomputes on save. A live RBA check converted USD 4.84 dated Saturday 9 Aug 2025 at Friday's 0.6519 to A$7.42. Migration `ForeignCurrency` adds the transaction fields and the `ExchangeRates` table.
- **Year guardrail.** Receipts dated outside the selected financial year show a warning with "Switch to FY…" and "Skip this receipt", and are labelled in the queue.
- **Viewer memory.** The owner reported a dark unpainted area after leaving full screen; automated Chrome did not reproduce it. Rendered pages are now capped at about 8 MP (full screen at 200% used 4096×5800, now 2377×3367). The previous bitmap is released before each redraw, and leaving full screen resets to fit-to-width.
- **Tests.** 51 API, 9 real-service integration and 17 frontend tests pass. API tests stub the RBA download. Both themes pass 60 screen/size combinations with zero axe violations. The guardrail and currency panel were checked in the browser.

**Deduction categories and quick review (11 October 2026)** ([ADR 0009](../adr/0009-categories-and-quick-review.md), [rule basis](../tax-rules/work-expense-categories-v1.md)).

- **Categories.** Seven new categories are calculated for FY 2024–25 and 2025–26: self-education (D4, K/I), work clothing (D3, C/N/S/P), tools and equipment, and software and subscriptions (D5; up to $300 an item, more than 50% work use), phone and internet (D5), gifts and donations (D9; whole amount, at least $2) and managing tax affairs (D10 M). Labels and codes were checked in both years' instructions and paper returns.
- **Data.** Migration `ClaimCategories` adds rule metadata, `ExpenseAllocation.ClaimType` and `ReceiptTransaction.SuggestedCategory`.
- **Minimum human action.**
  - AI suggests categories: on new readings, and from merchant names alone for older ones.
  - Quick review approves clearly read receipts by category group in one owner action.
  - The summary gains per-section myTax totals, **Approve all**, and batch rule review for administrators.
  - Step 2 offers every category, with its own declaration and claim type. "Fully allocated" now shows only for saved allocations.
- **Tax packs.** Packs carry one entry per myTax section, with per-section progress. Older packs show as a single D5 E entry.
- **Tests.**
  - 57 API, 9 real-service integration and 18 frontend tests pass, and the EF model-drift check is clean.
  - The quick review page has zero axe violations and no horizontal overflow at 1440 px or 390 px.
  - A full browser flow on synthetic demo fixtures approved 5 claims ($1,630.49) across D4/D5/D9.
  - The fixtures and the test rule reviews were removed afterwards. The database was backed up before migrating.
- **Owner action.** As administrator, review the new FY 2025–26 rules once: in Quick review, or under Summary → Administrator.

**Rule packs: yearly rules as data (11 October 2026)** ([ADR 0010](../adr/0010-rule-packs.md)).

- **Rules as data.**
  - Rules are versioned rows (`DefinitionJson`, `IsActive`, `PackId`) with parameters, checks, a decimal formula language, worked examples and evidence quotes.
  - The built-in FY 2024–25 and 2025–26 rules are expressed as packs with the same version names and results.
  - Every caller now loads rules from the database (`TaxRuleSet`).
- **Yearly updates.**
  - A background worker checks daily from June of the year a return covers until the ATO instructions appear, then drafts the year with AI.
  - The AI fills last year's shape only; it reads twice and the two readings are compared.
  - Verbatim quotes are verified against the downloaded PDF and guidance pages.
  - An administrator reviews a field-by-field comparison on `/rules` and activates. Activation creates new immutable versions and records their reviews.
  - Import and export cover formula changes the AI can't make.
- **Data.** Migration `RulePacks` adds the rule-definition columns and the `RulePacks` and `RuleSourceChecks` tables.
- **Tests.** 63 API (formula safety and decimals, pack validation, quote verification, fake-AI drafting end to end, import/export/supersede), 9 real-service integration and 19 frontend tests pass. The database was backed up before migrating.

**Skip, undo, delete and an optional step 2 (11 October 2026).**

- **Skip.** "Not a work expense" is now **Skip — not for my tax return**, with an optional reason: Personal, Not deductible, Duplicate, Other year or Other. The reason is stored as `ReceiptTransaction.SkipReason` (migration `SkipReasons`) and audited. Quick review's "Not for my tax return" records Personal.
- **Undo and Skipped.** After a skip, a 10-second **Undo** message appears. It is a shared toast that survives navigation. Review has a **Skipped** filter (`state=skipped`), and list chips show the reason.
- **Delete.** **Delete this file** (`POST /api/documents/{id}/delete`, explicit confirmation) removes a mistaken upload: the original, its reading, jobs, and any expenses and evidence links. Import items are kept but marked deleted, and a `DocumentDeleted` audit event keeps the name, checksum and facts. Deleting is refused when the file is in a tax pack, behind any recorded claim decision, supporting another claim, or being read. The UI warns about the ATO's five-year record keeping.
- **Optional step 2.** After confirming, an optional step 2 card offers **Split into expenses**, **Skip** and **Next receipt**, and points to Quick review. The editor opens straight away only when the receipt already has expenses.
- **Tests.** 66 API, 9 real-service integration (including the skipped list on PostgreSQL) and 20 frontend tests pass. A phone-size browser flow (skip with reason → Undo → optional step 2 → delete) had zero axe violations. Its synthetic fixtures were removed.

**Fewer steps, and no guessing (12 October 2026).** The owner asked for automatic filling wherever possible, and for blocking fields to be pointed out rather than left to guess.

- **Prefilled step 2.** Step 2 starts with one expense already filled in: the whole amount, the merchant and the category. `POST /api/transactions/{id}/prefill` returns the AI suggestion. If there isn't one yet, it asks from the merchant name with consent. Work use and claim type default to the owner's last approved choice for that category.
- **Optional notes.** The allocation "basis" is now optional notes. When left empty, the server records what the choice already says: the confirmed declaration, "kept for advice" or "excluded".
- **Specific errors.** Server validation names the expense and the field ("Expense 2: add a short description").
- **Pointing at the problem.** Save, Confirm, Approve and Approve-all buttons stay enabled. Pressing one with something missing scrolls to the field, focuses it, outlines it and shows the reason beside it (`core/attention.ts`); the outline clears when the field changes. This covers receipt confirmation, step 2, Quick review, Summary approvals, batch rule review and rule-pack activation. Rule review notes are pre-written.
- **Phone fix.** On phones the receipt action bar no longer overflows: "Skip" is shortened, and the full label stays for screen readers.
- **Tests.** 68 API, 9 real-service integration and 20 frontend tests pass. A phone-size browser check confirmed focus and outline on an empty amount and on an empty description, the prefilled category, and saving without notes, with zero axe violations.

**Owner feedback round (12 October 2026).**

- **Supporting documents.** "Manage evidence" is now "Add supporting documents". It sits under each claim's *Basis, rule and evidence* and opens right there, not at the page bottom. The section says the receipt is attached automatically, suggests what helps for each category, excludes the receipt itself, and searches all documents on the server.
- **Tax pack.** Preparing a tax pack needs one tick. The note is optional (it defaults to "Approved in KuTax."), and the two checkboxes are merged. The Prepare button points at the tick instead of being disabled. Verified end to end against PostgreSQL in the export integration test.
- **Account menu.** The menu now closes on any press outside it, through a document listener. The old transparent backdrop was confined to the header's backdrop-filter containing block. Checked at 1440 and 390 px.
- **Review progress.** Review shows "N of M done (x confirmed · y skipped)", and the Skipped count badge reads in both themes.

**Supporting uploads and the year at a glance (12 October 2026).**

- **Supporting documents.**
  - Documents and import batches carry a `Purpose` (migration `DocumentPurpose`): Receipt or Supporting. A supporting document is uploaded straight onto a claim from its evidence panel.
  - Supporting documents are malware-scanned and checksum-verified. They are never sent to AI, never get a transaction and never enter the review queue, receipt counts, undated-receipt exceptions or "read again".
  - The evidence picker lists them with `?kind=any`. Evidence can be linked while its scan is pending; the claim shows "needs evidence" until the scan clears.
  - A new real-service integration test covers this end to end.
- **Year at a glance.** The pre-freeze screen is now "FY … at a glance":
  - deductions you can claim, explicitly not a refund;
  - each myTax section with its whole-dollar entry and claim type;
  - counts of receipts, claims, skipped and kept for advice (`ExportPreview.Counts`);
  - other amounts explained under a fold.
- **Tests.** 68 API, 10 real-service integration and 20 frontend tests pass, with zero axe violations on the new screen. A browser upload onto a demo claim confirmed the document is attached. That also reset that synthetic demo claim's approval, as designed.

**Self-directed polish round (12 October 2026).**

- **Year picker.** The native financial-year `<select>` is replaced by a themed list (`core/year-picker.ts`). It supports the keyboard (arrows, Home/End, Enter, Escape) and closes on any outside press. It lists the current year, the three before it, and any year with records (`GET /api/tax-years`), with "This year" and "Has records" hints.
- **Finished tax packs.** A finished pack shows themed confetti (static under reduced motion) and a "Your tax pack is ready" panel. That becomes "All entered and checked" once every myTax section is marked checked. "Rules applied" lists only the rules behind approved claims; the rest are folded away. Sources are labelled "ATO guidance" or "ATO ruling" correctly.
- **Quick review "closer look" list.** The list now names each receipt with its own reason: still being read, missing amount, not yet in AUD, possible duplicate, typed by hand, unsure merchant/date/amount, or missing date. Undated receipts are listed in every year. A possible duplicate offers one-tap **Skip as a duplicate** on the receipt.
- **Fixes.**
  - Summary claim rows overflowed their card on phones; long rule versions could not shrink inside the grid.
  - The retro theme's `.figures div` rule outlined every nested box on the tax pack page.
  - Classic-theme contrast on the review progress line.
  - The account menu now closes on outside clicks.
  - "Review now" on the inbox reads "All caught up" when nothing is waiting.
  - "Re-check with AI" is hidden while a draft for that year is running.
- **Verification.** 12 pages × 3 sizes × 2 themes (72 checks) show no horizontal overflow, no card overflow and no axe violations. 68 API, 10 real-service integration and 20 frontend tests pass.

**Simpler downloads (12 October 2026).** Tax-pack files appear as four plain tiles: Summary (PDF), myTax guide (PDF), Spreadsheet (Excel) and Receipts (ZIP). Each has a one-line purpose and its size, in a shared `claims/pack-files.ts` used on Summary and History. Checksums, snapshot IDs and "Integrity details" are no longer shown in the UI; they remain in the PDF, workbook and evidence manifest for verification. The year picker's list now floats over the page; its styles live in its own component, after the shell's `.menu` styles turned out not to reach it. Both were verified open and closed in both themes at desktop and phone sizes.

**English and Chinese (12 October 2026).** The whole interface switches between English and Simplified Chinese from the account menu (中/EN) or Settings → Appearance, without reloading; the choice is saved on the device (ADR 0011). About 1,000 strings are translated, including server messages, rule conditions and declarations, page titles and the "needs attention" notes; myTax section names stay in English so they match myTax. The Chinese dictionary loads only when used (initial bundle 454 kB). Every tax pack now contains seven files: summary, myTax guide and spreadsheet in English and in Chinese, plus the receipts ZIP. The download tiles follow the interface language with an English / 中文 toggle. The documents were simplified: a one-page summary with the total and "what to enter in myTax"; an entry guide that lists every receipt per section with a tick box, date, description, amount and receipt file name; a spreadsheet that opens on a summary sheet, with technical details on a later sheet.

- **Verification.** 69 API tests (including `TaxPackFileTests`, which opens all seven generated files), 10 real-service integration tests and 22 frontend tests pass (one integration test, `Reading_again_fills_untouched_manual_drafts…`, failed once under full-suite load and passed on its own and on the full rerun, so it's timing-sensitive). A Chinese browser audit at phone (Retro) and desktop (Classic) across 13 pages found no overflow and no axe violations. The English text it still reports is user data or deliberate myTax names.

**Readable evidence names and a plain spreadsheet (12 October 2026).** Receipts in the evidence ZIP are named `originals/Merchant_yyyy-MM-dd_CUR amount_Category.ext`, with the total as printed on the invoice (for example `Anysphere, Inc._2025-08-09_USD 19.50_Software and subscriptions.pdf` or `Telstra_2026-03-22_AUD 34.00_Phone and internet.pdf`; categories in English, joined with "+" when a receipt is split). Supporting documents add `_supporting` before the category, repeats get ` (2)`, and characters that file systems reject are replaced. The guide and spreadsheet "Receipt" columns use the same names. The spreadsheet no longer has IDs, version GUIDs, user IDs, timestamps or SHA-256 columns. The former technical "Details" sheet is now "All expenses" (deductible in column 9, status in 10, translated headers). Rules and Evidence keep only the category, section, conditions, ATO source and check date, and the file name and size. The ZIP's `manifest.json` and `snapshot.json` keep the full technical record. 69 API and 10 real-service tests pass.

**AI and the person take turns (12 October 2026).** While a receipt is queued or being read, the review form, Confirm, Skip, manual entry, Read again and Delete are disabled. The API refuses the same writes with 409, so AI and a person never edit a receipt at once. A receipt still waiting in the queue (for example behind an AI rate limit) offers "Don't wait — I'll enter it myself": the job is cancelled atomically only if it hasn't started, and is audited as `ProcessingTakenOver`. After a receipt is confirmed, the review page opens the next one that still needs confirming. Every receipt was already confirmed in the synthetic account, so the auto-advance was covered by a unit test rather than tried in the browser. 70 API, 10 real-service and 23 frontend tests pass.

**Themed date field and fuller user management (13 October 2026).**
- The receipt date uses `DateField` (`core/date-field.ts`) instead of the browser's unstyleable date picker. It's a typed text box that accepts dd/mm/yyyy, yyyy-mm-dd and 2026年8月1日. It has a themed calendar: Monday first, localised month and weekday names, later than tomorrow disabled, keyboard navigation, and it closes when you press outside. It works with `ngModel` and "needs attention".
- User management shows Edit, Reset password, Disable/Enable, Unlock and Delete on each other account's row (ADR 0002 amendment). Reset and delete open inside the row. Accounts with records can only be disabled.
- 71 API, 10 real-service and 28 frontend tests pass.

**Finding receipts, saving feedback and resilience (13 October 2026).**
- **Search and sort:** the review queue has a search box (merchant, file name, amount including the foreign amount on the receipt, or date) and a themed sort menu (`core/select-menu.ts`) over newest added, receipt date either way, merchant A–Z and amount. Both run on the server across all pages and are remembered for the session.
- **Duplicates:** the duplicate check now also matches same merchant and date where one copy's AUD amount equals the other's foreign amount. Copies already skipped don't count.
- **Bug fix:** allocation amounts such as 71.90 were refused by a floating-point check. They're now compared with a tolerance.
- **Busy buttons:** a busy button shows a spinner (`.btn[aria-busy=true]`) on the main save and approve actions. Allocation inputs are locked while saving, and a toast confirms the save.
- **Back:** review has a Back button to the page that led there (`core/return-to.ts`), restoring the scroll position.
- **Retries:** reads retry transient failures (0/502/503/504) for about 25 seconds, so a deploy or restart no longer shows "couldn't load". This was checked by stopping the app container for 6 seconds mid-navigation.
- **Tests:** 71 API, 10 real-service (including search, sort and the cross-currency duplicate on PostgreSQL) and 32 frontend tests pass.

**Year filter, batch skip/restore and claim status filter (13 October 2026).**
- **Year filter:** the review queue has an "Only FY …" toggle. `GET /api/documents?year=` keeps receipts dated in that financial year; undated ones are left out.
- **Batch skip and restore:** a Select mode adds checkboxes and a sticky bar. It skips the ticked receipts with a reason, or restores them in Skipped, through `POST /api/documents/skip-batch` (1–200 receipts, owner-scoped). Each receipt follows the single-skip rules: not while being read, not when blocked by the malware check, and not when split into expenses. The response lists the ones that couldn't change and why, and each change is audited. Bulk category changes were left out on purpose, because Quick review already categorises and approves in groups under the rule checks.
- **Claim status filter:** the summary breakdown filters claims by status group (ready to approve, needs details, rule not checked, needs advice, approved, excluded), with counts. The to-do tile's "See them" opens it on the claims ready to approve.
- **Tests:** 72 API, 10 real-service (year filter on PostgreSQL) and 33 frontend tests pass.

**Cloud Run deployment, just-in-time background work and undo (13 October 2026).**
- **Cloud Run:** `scripts/Deploy-CloudRun.ps1`, `infra/cloudrun/*` and `docs/operations/cloud-run.md` deploy KuTax to Cloud Run (ADR 0012): the app with ClamAV as a sidecar, Supabase PostgreSQL and Cloudflare R2. The service scales to zero, and credentials live in Secret Manager.
- **Code changes for it:**
  - data-protection keys can live in PostgreSQL, certificate-encrypted (migration `DataProtectionKeys`; the local database was backed up first);
  - a configurable S3 signing region, with checksums sent only when required;
  - a managed-services production mode;
  - yearly rule checks triggered only when someone opens a year that needs them (no timer).
- **Undo changes:** the receipt review has an "Undo changes" button that appears once a field is edited and restores the last loaded or saved details.
- **Flaky test fixed:** `ReReadIntegrationTests` no longer races the running app's worker on the shared database.
- **Verified locally:**
  - sessions survive deleting and recreating a container that keeps its keys in the database;
  - the baked ClamAV image answers in 9 seconds;
  - 78 API, 10 real-service (three consecutive runs) and 34 frontend tests pass.
- **Not yet run:** real Cloud Run, Supabase and R2.

**Sharing tax records (13 October 2026, ADR 0013).**
- **Inviting:** from Settings → Sharing, anyone can invite an email address to act fully on their tax records. The invitation is emailed (bilingual, via SMTP and MailKit; Mailpit locally), and a copyable link is always shown.
- **Accepting:** the invitee accepts through the link, or in the app if their account predates the invitation. A live link also allows signing up with the invited address when registration is closed.
- **While acting:** a coloured bar names whose records are on screen, and the account menu switches between people. The server checks every act-as request against an active delegation before any endpoint runs.
- **Transparency:** changes are attributed to the real actor, the owner sees what helpers did, and either side can end sharing at once.
- **Migration:** `Sharing` (Delegations table and `AspNetUsers.CreatedAt`; local database backed up first).
- **Checked end to end in the browser with synthetic accounts:** invite, email in Mailpit, sign-up from the link, accept, the owner's receipts shown under the acting bar, the owner stops sharing, and the helper is returned to their own records. The two synthetic helper accounts were then deleted through the new user deletion.
- **Tests:** 80 API, 10 real-service and 35 frontend tests pass.

**Simpler KuTax for first-time users (13 October 2026).**
- **Three places:** the main navigation is Home, Receipts and Tax. Tax combines the summary, this year's tax pack and every earlier pack; `/history` redirects there. Settings stay in the account menu.
- **One next step:** Home shows a single "next step" card (add receipts, approve or check them, finish the summary, prepare the tax pack, or done) with one button. It replaces the four-step checklist.
- **One-page receipt:** the receipt page asks "What kind of expense is this?" (with claim type, work share and the rule's declaration when needed). "Confirm and claim" confirms the details, creates the expense and approves it when the year's rule is checked. This uses the new `POST /api/transactions/{id}/claim`, which shares the quick-review code path and keeps every check. Splitting and reimbursements sit under More options.
- **Plain words:** snapshot becomes tax pack, allocations become expenses, evidence becomes attachments. People who aren't administrators no longer see rule checks or "ask your administrator". Financial years show their months.
- **AI asked once:** uploading starts as soon as files are chosen. The first time, if AI is set up but not agreed to, one question is asked ("Let AI read these receipts for you?") before uploading, and never again. A one-tap "Use AI" notice replaces the trip to Settings.
- **Downloads:** a tax pack shows the entry guide and "Download everything" (`GET /api/tax-exports/{id}/download?lang=`, one ZIP built in a self-deleting temp file) in the interface language. Single files are under a disclosure.
- **Settings tabs:** You, Sharing, AI, Look and language, and Administrator for administrators (`?tab=`).
- **Checked in the browser (Chinese, phone and desktop):** navigation, the next-step card, Tax with its packs, the language-specific "download everything" ZIP and the settings tabs. A synthetic receipt was uploaded without "Start import", the AI question was answered, the receipt was typed and claimed in one press, and the test receipt was then deleted.
- **Tests:** 81 API, 10 real-service and 36 frontend tests pass.

**History, reports, profiles and administration (14 October 2026).**
- **History is back** in the main menu (Home, Receipts, Tax, History). Tax keeps only this year's work, with a one-line pointer to the latest pack. History lists every pack, year by year and version by version, with quick filters: a year, all versions, latest only, or not entered in myTax yet (`?year=`).
- **Reports** (account menu, `/reports`, `GET /api/reports/{year}`): totals; spending and claimed deductions month by month; deductions by kind of expense; how expenses stand; top merchants; currencies; and deductions claimed year by year. Charts are themed inline SVG. Figures come from the same annual assessment as the tax summary, for the records being worked on (including shared ones).
- **3D reports:** the monthly, kind-of-expense and year-by-year charts are drawn as outlined "building blocks" by default (`core/iso.ts`: oblique boxes and a tilted ring with thickness, no perspective, so heights stay proportional), with a Flat option remembered on the device. Blocks rise in on load, except with reduced motion. Phones get a narrower, taller monthly drawing. The progress bar is a 3D slab and merchants are 3D bars lying down. Every chart shows a tooltip beside what's hovered (tap to pin on phones): month amounts, a category's claimed/ready/share, a state's count and share, a merchant's share and average, and a year's change on the year before. Chart drawing styles live in `src/charts.scss` to keep the component within its style budget.
- **Sharing suggestions:** the invite box suggests people shared with before, then other accounts (`GET /api/sharing/people`), with keyboard support.
- **Personal details:** optional mobile number, occupation and profile photo (`PUT/DELETE /api/accounts/me/avatar`, a `UserAvatars` table). The header shows the photo. Email is read-only: it's the sign-in, so a different address means a new account.
- **User management:** make or remove administrators (`PUT /api/users/{id}/administrator`, which signs them out), private administrator notes (`PUT /api/users/{id}/note`) and join dates. Administrators no longer edit other people's names or emails; `PUT /api/users/{id}` only enables or disables.
- **Fixes:** the settings tabs no longer share the main menu's `.tabs` class, whose retro hover turned the label white on white.
- **Migrations** `ProfileDetails` and `AdminNotes` were applied locally after a database backup.
- **Checked in the browser (Chinese, phone and desktop):** History filters, Tax pointer, Reports, settings, sharing suggestions and users, with no page errors or horizontal overflow. A synthetic photo was added, shown in the header, and removed.
- **Tests:** 85 API and 40 frontend tests pass. The real-service integration suite was not rerun for this change.

**Reports that help you claim more (14 October 2026).**
- **No more 3D/flat switch:** reports are always the building-block charts. Animation still respects reduced motion.
- **Missing receipts:** `GET /api/reports/{year}` returns `missing`. These are merchants with receipts in at least 3 months that cover at least half the span from their first to last month this year, with the months in between that have no receipt. They're shown as dashed month chips with the usual amount and kind of expense. Quarterly and occasional spending isn't flagged.
- **Compared with last year:** `compare` lists kinds of expense approved last year with nothing, or less than half, claimed or ready this year.
- **Drill-down:** report items open what they stand for:
  - months, kinds of expense and merchants open the receipt list narrowed with the new `GET /api/documents` filters `month=yyyy-MM`, `merchant=` (exact, any case) and `category=`;
  - the progress slab opens the tax summary, and years open History.
  - With a mouse, hover shows the figures and a click opens. On touch, the first tap shows the figures and a second tap opens.
  - The receipt list shows "Showing …" with "Show all". The view survives opening its receipts and is dropped when Receipts is opened from the menu.
- **Deadline:** from 1 July after the chosen year until a month after 31 October, Home's next-step card shows the days left to lodge yourself. It warns near the end and points to registered tax agents afterwards.
- **Tax estimate:** `KuTax.Core.Claims.TaxRates` holds the resident scales and the 2% Medicare levy. FY2024-25 and FY2025-26 have the 16% second rate; FY2026-27 has the legislated 15%. The table was checked against the ATO resident rates page on 14 October 2026. Reports estimate the tax saved at the chosen income band only after the person picks one (remembered on the device), and say it's a rough guide. It isn't used for any claim. Years without a checked scale show no estimate.
- **.NET 10.0.13 servicing:** see DEPENDENCIES.md (security advisories in `System.Security.Cryptography.Pkcs/Xml`).
- **Tests:** 86 API, 10 real-service integration (receipt-list filters added) and 43 frontend tests pass. Two failed integration runs, caused by a cleanup-order bug in the new test (since fixed), had left 4 synthetic `list-…@kutax.test` accounts in the local database; they were removed.
- **Checked in the browser (Chinese, phone and desktop):** the estimate ($3,110 at 32% = $995.20), missing months and last-year cards, a month opening its receipts with the chip, the 17-day deadline badge, and the menu clearing the chip. The richer report used synthetic data substituted in the browser.

**First production deploy and CI/CD (15 October 2026).**
- **Production:** Cloud Run project `kutax-prod` (`australia-southeast1`), Supabase session pooler (Sydney), Cloudflare R2 bucket `kutax-media`. The first deploys found four problems, all fixed:
  - **Credential length:** the production check required a 24-character database password, and the error didn't say which credential was short. The minimum is now 16 characters (owner's decision: a random 16-character password over TLS with sign-in limits is out of reach of guessing), and each credential has its own message.
  - **Empty prompt:** an empty hidden-prompt entry was submitted as a secret. The deploy script's prompts now re-ask when input is empty or too short, and the temporary administrator password is typed twice.
  - **Startup probe:** Cloud Run's HTTP startup probe doesn't send the public host name, so host filtering answered 400 and the instance never counted as started. The startup probe is now TCP on 8080, and the deploy script checks `GET /api/health/ready` on the public URL afterwards.
- **Forwarded headers:** behind Cloud Run every page answered "HTTPS is required". Google's front end terminates TLS and forwards from addresses that aren't fixed, so `X-Forwarded-Proto` wasn't trusted. With `Production:ManagedServices`, forwarded headers are now trusted for the last hop only (`ForwardLimit` 1). The container is reachable only through Google's front end, and the client address used for sign-in rate limits is the one Google appends. Self-hosting keeps loopback plus the explicit `ReverseProxy:KnownProxies` list.
- **Migrations:** the setup job applied migrations to the production database and created the first administrator. The temporary password secret was then deleted.
- **CI/CD:**
  - `deploy.yml` releases `main` after **Build and test** passes on a push, or when started by hand. There's no schedule: ClamAV's freshclam updates signatures when an instance starts.
  - Sign-in uses Workload Identity Federation limited to `KunLee/kutax` on `refs/heads/main`, with a least-privilege `kutax-deployer` account (`scripts/Setup-GitHubDeploy.ps1`). `Deploy-CloudRun.ps1 -Unattended` never prompts.
  - Branches: work on `dev`, release by pull request `dev` → `main`. CI rejects pull requests into `main` from other branches (`merge-source`).
- **CI fix:** the integration tests assumed the bucket already existed. On CI's empty RustFS, tests that ran before `FoundationTests` failed with NoSuchBucket. An assembly fixture now migrates the database and creates the bucket once, before any test. It was reproduced and verified locally with an empty database and a new bucket: 10 of 10 pass.
- **README:** banner and icon (`docs/assets/`), feature table, and screenshots from the synthetic demo account (`docs/assets/screenshots/`). The favicon now matches the icon.
- **Fixes:**
  - The phone estimate card's text collapsed into a narrow column in Chinese.
  - The tax summary said "KuTax never estimates a refund"; it now points to the Reports estimate instead.

**Own domain: kutax.com.au (15 October 2026).**
- **Worker:** a Cloudflare Worker (`infra/cloudflare`, wrangler 4.151.0) serves `kutax.com.au` and `www`. It forwards to Cloud Run's `run.app` address with a shared secret (`X-KuTax-Origin-Verify`, Secret Manager `kutax-origin-secret`) and the visitor's address (`X-KuTax-Client-IP`). It redirects `www`, caches only hashed build files, and leaves query strings out of logs. 5 Node tests and a `wrangler deploy --dry-run` run in CI.
- **App:** with `Origin:SharedSecret` set, it serves only requests carrying the secret (fixed-time comparison) and takes the client address from the Worker only then. Production on a non-`run.app` domain refuses to start without a secret of at least 32 characters. `AllowedHosts` holds the domain and the `run.app` name.
- **Deploy script:** `-PublicOrigin` creates or uses the secret. A deploy by hand checks readiness directly on Cloud Run (with the secret), then through the domain.
- **Home page:** before the first receipt, there's no next-step card. The inbox is that step, and the lodging deadline moves to the financial-year line.
- **Verified in production:**
  - `https://kutax.com.au` and `/api/health/ready` answer 200, and `www` redirects with path and query kept.
  - The `run.app` address answers 403 without the secret or with a forged one.
  - The sign-in page loads on phone and desktop with no errors.
- **Tests:** 91 API and 44 frontend tests pass.

Remaining for the M6 gate: manual screen-reader and report-readability review, the AI evaluation (owner action), the real-receipt personal pilot after the owner reviews privacy/consent settings (owner action), a broad M5 regression after M6 changes, and hosted CI. Do not declare delivery-ready yet.
