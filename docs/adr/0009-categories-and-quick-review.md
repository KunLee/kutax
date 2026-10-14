# More deduction categories and one-action quick review

## Context

KuTax calculated only union fees and professional memberships. Every receipt needed three human steps, each on its own screen:

1. confirm the facts;
2. split them into expenses;
3. approve in the summary.

The owner asked for the least human action possible. The handoff requires the following:

- AI suggests, rules calculate and humans approve, with no AI auto-approval;
- "avoid compulsory manual categorisation for every receipt";
- "bulk-review where safe".

## Decision

- **Categories.** These categories are calculated for FY 2024–25 and 2025–26:
  - self-education (D4);
  - work clothing (D3);
  - tools and equipment, and software and subscriptions, up to $300 an item (D5);
  - phone and internet (D5);
  - gifts and donations (D9);
  - managing tax affairs (D10).

  Each rule carries its myTax section, paper reference, claim-type codes and limits. It also carries the owner declaration shown in the UI. Sources and hashes are in `docs/tax-rules/work-expense-categories-v1.md`. `ExpenseAllocation.ClaimType` stores D3 and D4 codes.
- **AI category suggestions.**
  - **New readings.** Extraction now returns a suggested category, validated against a fixed list, and the suggestion is stored on the transaction (`SuggestedCategory`). It is never used to calculate anything.
  - **Earlier readings.** For receipts read before this change, the quick review page asks the text model once, sending merchant names only. This uses the same consent as receipt reading.
- **Quick review** (`/review/quick`, `/api/quick-review/{year}`). It lists receipts that can safely be reviewed together. Each one must:
  - be read by AI with confidence of at least 0.8 for merchant, date and amount (or already confirmed by the person);
  - be in AUD (or already converted);
  - not be a possible duplicate, still processing, or already split.

  The owner then works through groups by category:
  - fixes any category;
  - sets work use and claim type once per group;
  - ticks the group's declaration;
  - approves the batch in one request.

  The server re-checks that every item is still eligible and unchanged. It then confirms the transactions, creates one full-amount allocation for each, and records approvals for exactly the claims the rules assess as candidates. Each approval is attributed and audited (`via: QuickReview`). Categories without a reviewed rule are confirmed and allocated, and wait in the summary. "Not for my tax return" is the same as **Not a work expense**. Everything unusual stays in the one-by-one review.
- **Summary.**
  - The summary shows totals for each myTax section, with copy buttons.
  - It offers **Approve all** for waiting claims: one owner confirmation, then each claim is approved with its own version checks.
  - Administrators can review a year's unreviewed rules together (`POST /api/tax-rules/review-batch`), with one note and an explicit confirmation.
- **Tax packs.**
  - `YearAssessment.Sections` gives one entry for each label, and `EntryAmount` is the sum of those entries.
  - Entry progress is tracked per section.
  - The PDF, workbook (appended columns) and manifest list every section.
  - Older snapshots without sections show as one D5 E entry.

## Consequences

The human stays the approver, but a typical year needs a few taps per category instead of three screens per receipt. Reimbursed or split receipts, receipts that aren't AUD and items over $300 still need the one-by-one review.
