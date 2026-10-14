# Tax rules as yearly rule packs, drafted by AI and activated by an administrator

## Context

ATO rules change by financial year: limits, rates, labels and claim-type codes. Rules were C# constants, so every change needed a developer and a release. That doesn't work once KuTax runs in the cloud without a development environment. The owner asked for formulas to be data too, with AI reading each year's new documents.

The handoff still requires deterministic, verified, FY-specific rules with sources, review and regression tests. It forbids auto-approval.

## Decision

- **Rule packs.** A financial year's rules are a JSON document (`RulePackDocument`, schema version 1). Each category has:
  - its label, myTax section, paper reference and claim-type codes;
  - its conditions and owner declaration;
  - its official source;
  - named **parameters**, **checks** (condition → Unsupported/NeedsReview/Excluded, with a reason) and a **deductible formula**;
  - **worked examples** and **evidence quotes**.
  The built-in FY 2024–25 and 2025–26 rules are expressed this way and seeded unchanged in name and result.
- **Formula language** (`Formula`). Formulas are written in a tiny, side-effect-free expression language evaluated with `decimal`:
  - operators: `+ - * /`, comparisons, `and/or/not`;
  - functions: `round2` (midpoint away from zero), `min`, `max`, `if`.
  It is parsed and type-checked; length, nodes and depth are capped, and it can't call code or read data. Formulas may use the fixed inputs (`amount`, `workUsePercent`, `reimbursement`, `receiptTotal`) and the rule's parameters. A new kind of input, such as hours or kilometres, needs a release because KuTax has to collect it.
- **Rule versions in the database.** Rules live in `TaxRules`, one immutable version per year and category. Activating a pack:
  - marks that year's previous versions inactive (they are kept for history and frozen tax packs);
  - inserts `AU-{year}-{category}-v{n}`;
  - records a review for each new version, because activation is the administrator's check.
  At most one version per year and category is active (a filtered unique index). Claims approved under an older version need re-approval.
- **Validation before activation.** A pack must:
  - pass schema and range checks;
  - use official ATO sources only;
  - have formulas that parse and type-check;
  - reproduce **every worked example**.
  Examples may pin their own parameter values, so they test the formula's logic and keep passing when a limit changes.
- **AI drafting** (`RulePackDrafter`). Once a year, from 1 June of the year a return covers, a background worker checks daily until the ATO publishes that year's instructions, then queues a draft. Administrators can also check, draft or re-check a year by hand. For each category the drafter:
  - sends last year's rule, the matching instruction pages and the category's ATO guidance page to the configured text model, under a strict JSON schema (public ATO text only, nothing personal);
  - lets the model **only fill last year's shape** with this year's values and verbatim quotes (it never writes formulas, and suggests method changes only as a warning);
  - asks twice independently and flags any disagreement.
- **Report** (`RulePackReview`). Before anyone sees a draft, each quote must be found word for word in the downloaded source, ignoring spacing and punctuation. A parameter's quote must also contain its number. Unverified quotes and missing quotes for parameters block activation. The report compares the draft field by field with the rules in force and lists warnings.
- **Import and export.** For changes the AI can't make, such as a new formula or check:
  1. export a year's pack;
  2. edit it, or obtain a reviewed pack;
  3. import it.
  Imported packs are validated and compared the same way. Their quotes are marked "not machine-checked".
- **UI.** Administrators manage this on `/rules`, which shows the yearly status, updates, a field-by-field comparison with quotes, and activate or reject. Summary shows a banner when an update is ready.

## Consequences

Most yearly changes need no code: values, labels, codes, conditions, and formulas within the language. The trust chain stays intact: AI drafts, the program verifies sources and examples, an administrator activates, and owners still approve each claim. New inputs, new categories (the app must present them) and schema changes still need a release.
