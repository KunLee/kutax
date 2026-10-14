# Self-registration

The owner asked for public sign-up, with full features for registered users. This replaces the closed-registration decision in ADR 0002 for deployments that opt in.

- `Registration:Enabled` is off in `appsettings.json` and turned on in `appsettings.Development.json` and the local Compose app. A production operator must enable it on purpose. `GET /api/auth/registration` tells the UI whether to offer "Create account".
- `POST /api/auth/register` takes name, email and password. It requires CSRF and uses the same email, name and password rules as administrator-created accounts (12+ characters with mixed classes). It creates an ordinary member, signs them in and records the event in a structured log without credentials. Registering while signed in is rejected.
- Members get every workspace feature: import, review, allocations, claim approval and tax packs. User management and tax-rule review stay administrator-only. Administrator access is still provisioned by the operator (`--bootstrap-admin`) or by an existing administrator. Rule reviews are global, so once an administrator reviews a year's rules, every member can approve their own claims.
- Each client address may make 10 registration attempts per hour, including failed ones. The proxy's forwarded address is used when `ReverseProxy:KnownProxies` is configured.
- The UI requires the user to acknowledge that KuTax prepares records and figures, and does not give tax advice or lodge returns.

Accepted risks for now:
- No email verification. KuTax cannot send email, so an address is not proven to belong to its registrant.
- A duplicate-email attempt returns a generic 409. Rate limiting slows email enumeration but does not prevent it.
- No CAPTCHA and no MFA.

Revisit all three before opening registration on the public internet.
