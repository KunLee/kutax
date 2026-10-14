# 0013 — Sharing tax records with a helper

Status: accepted, 13 October 2026

## Context

People often help each other with tax: children for parents, partners, or a tax agent who needs to see the records. The owner wants a person to be able to share their tax records with someone who then acts **fully** on their behalf, with no separate view-only role. Invitations should arrive by email and also show in the app, and the invited person must accept. Records are reachable only after explicit sharing and acceptance, and only until either side ends it.

## Decision

- **Model.** A `Delegation` row records the owner, the invited email (normalised), the delegate once accepted, a status (Pending, Active, Declined, Revoked, Expired), a SHA-256 of a single-use link secret, and a 7-day expiry. There is at most one open invitation or active delegation per owner and address (a filtered unique index). Deleting either account removes it.
- **Acting for someone.** The SPA sends `X-KuTax-Act-As: <ownerId>` on data requests while acting. A middleware runs before authorisation and any endpoint. It accepts the header only for an Active delegation from an enabled owner to the signed-in user, and refuses everything else with 403 `DelegationEnded`, so no endpoint reads another person's data by mistake. When it accepts, it adds an owner claim. Feature endpoints read `Taxpayer.Owner(...)` for whose records, and `Taxpayer.Actor(...)` for who did it: audit `ActorId`, claim and tax-pack `ApprovedBy`. Account, sign-in, sharing and user-management endpoints always act as the signed-in person. Administrator rule reviews always record the administrator.
- **Accepting.** The emailed link proves the invitee owns the address. Registration doesn't verify email, so an in-app Accept is offered only to accounts that existed before the invitation was sent; newer accounts must use the link. Someone holding a live link may sign up with the invited address even when open registration is off. Links are single-use.
- **Email.** Any SMTP provider through MailKit (`Email:*` settings). The invitation is bilingual. Without email configured, the owner gets the link to send themselves. Locally, Mailpit captures mail at http://localhost:8025.
- **Transparency.** Either side can end sharing at any time. The owner sees a log of what helpers did, from the audit trail. While acting, a coloured bar names whose records are on screen, and the account menu switches between them. AI-reading consent can be given by the owner or a helper, and it is recorded against whoever gave it.

## Consequences

- Every change made on someone's behalf is attributable. Tax packs record who approved them.
- One role only: a helper can do anything the owner can in the app (not account, password or sharing settings).
- Tests cover refusal before acceptance, refusal for anyone else, attribution, ending, single-use links, and new accounts needing the link.
