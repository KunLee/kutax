# KuTax implementation guidance

- Read `docs/architecture/KUTAX_HANDOFF.md` before changing architecture or scope. It is the product baseline; do not create a competing root handoff.
- The owner has authorized implementation through the first end-to-end MVP. Work sequentially through M1–M6 gates without stopping for routine decisions; consult `docs/architecture/IMPLEMENTATION.md` for progress and known gaps.
- Keep Angular, ASP.NET Core, PostgreSQL/EF Core and private RustFS storage. Core has no infrastructure dependencies. Serve the SPA and API from one production app image.
- Use the latest stable releases of frameworks, direct dependencies, tooling and service images. Verify upstream releases when adding/upgrading them, pin reviewed versions and refresh lock files. Never use previews/RCs/nightlies. Document any compatibility constraint explicitly in `docs/architecture/DEPENDENCIES.md` rather than forcing an unsupported combination.
- Never commit credentials or real financial records. Use synthetic test data. Enforce authentication, resource ownership and CSRF validation on cookie-authenticated writes.
- AI suggests; deterministic verified FY-specific rules calculate; humans approve. No ATO lodgment; unsupported scenarios must remain excluded from confirmed claims.
- Run the relevant backend/frontend checks and update implementation notes. Explicitly distinguish unavailable integration checks from passing tests.
