# Mobile-first UI and design system

The owner mostly uses KuTax on a phone. The UI follows the Figma export in `docs/figma/Kutax`:
- Manrope and DM Mono fonts.
- Ink borders with hard offset shadows.
- A purple, sky, mint, orange and yellow palette.
- Three primary screens: Inbox, Review and Summary.

The Figma file is a React/Tailwind prototype, so we copied its visual language into Angular rather than its code. No component library or Tailwind was added. Design tokens and shared classes (`.card`, `.btn`, `.field`, `.chip`, `.item`, `.notice`) live in `frontend/src/styles.scss`, so each component's styles stay within the 8 kB budget.

Navigation uses the Angular router, so the phone back button and deep links work. Inbox loads eagerly. Review (`/review` and `/review/:id`), Summary, Settings and Users load lazily. Below 960px the main navigation becomes a fixed bottom tab bar with safe-area padding. The sticky header drops `backdrop-filter` at that width; otherwise the header would become the containing block for the fixed tab bar. Review is a two-pane list and detail on desktop, and a list followed by a full-screen detail on phones.

Changes from the Figma prototype for accessibility and honesty:
- Text is at least 12px.
- Form controls use 16px text so iOS doesn't zoom in on focus.
- Touch targets are at least 44px.
- Small text uses darker purple and grey to keep AA contrast.
- Badges use ink text on orange.
- Motion is disabled under `prefers-reduced-motion`.

Every number on screen comes from the API. The Figma placeholders "68% tax readiness", "$3,842 draft deductions", "encrypted and stored in Australia" and HEIC support are not shown. HEIC isn't supported, and storage location and encryption depend on the host. In their place the UI shows:
- the share of readable receipts confirmed;
- approved deductions from the deterministic assessment;
- a deduction mix that counts only approved claims and claims awaiting approval;
- the claim "Private storage".

The default manual extractor is never presented as having read a receipt. Per-field "Looks clear / Check this" prompts appear only for values that an extraction actually suggested.

`GET /api/documents/` now returns `reviewState`, `merchant`, `date` and `amount` for each item, owner-wide `counts`, and an optional `state=review` filter. These drive the review queue and badge. Confirmation lives on the transaction, while document status stays `NeedsReview`.
