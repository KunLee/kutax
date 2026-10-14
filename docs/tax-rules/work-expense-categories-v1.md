# Work-expense categories — reviewed implementation basis

Verified against official sources on 11 October 2026. This covers FY 2024–25 and FY 2025–26 only.

Each category and year has its own immutable `AU-{startYear}-{category}-v1` rule, now expressed as a built-in rule pack (parameters, checks, formula and worked examples; see [ADR 0010](../adr/0010-rule-packs.md)). Later years and revisions arrive as activated rule packs. Union fees and professional memberships keep their existing basis (see [periodic-employment-fees-v1.md](periodic-employment-fees-v1.md)).

An administrator must still record a source review in KuTax before claims under a rule can be approved. This document records engineering verification, not a professional tax opinion.

## Year mapping

The question numbers and labels were checked in the official individual tax return instructions and the paper return for both years:

- D3 label C, with claim-type codes C, N, S and P;
- D4 label D, with claim-type codes K and I;
- D5 label E;
- D9 label J;
- D10 label M.

The labels and codes are the same in 2025 and 2026. The 2025 paper return also has D10 label N (ATO interest), which KuTax doesn't use.

The instructions were re-downloaded on 11 October 2026. Their hashes differ from those recorded on 10 October, because the publisher re-issued the files. The new rules record the new hashes.

| FY | Publication | SHA-256 (11 Oct 2026 download) |
|---|---|---|
| 2024–25 | [Individual tax return instructions 2025](https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y) | `BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9` |
| 2025–26 | [Individual tax return instructions 2026](https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y) | `07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3` |
| 2024–25 | [Paper return 2025](https://iorder.com.au/publication/Download.aspx?ProdID=2541-6.2025&dl=Y) | `FB6E7AB23479344BCD672D86B87CD57D0AD41ECDE171D6DAE12C617C70CB8851` |
| 2025–26 | [Paper return 2026](https://iorder.com.au/publication/Download.aspx?ProdID=2541-6.2026&dl=Y) | `C32165AAA3495FEE11839B2829DC5B4DBE9C3429DCF24581D02582875B263374` |

Paper references are not import codes. Users confirm the current myTax screens themselves.

## Categories

All categories use the same calculation:

- the GST-inclusive amount × work-use %, rounded to cents (midpoint away from zero);
- minus the reimbursement attributable to the work portion.

The conditions below are enforced deterministically. Everything else is confirmed by the owner through the category's declaration, which is stored as the allocation basis.

| Category | Label | Enforced | Owner confirms | Not calculated |
|---|---|---|---|---|
| `SelfEducation` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/education-training-and-seminars/self-education-expenses)) | D4 D | Claim type K or I | Employed when incurred; sufficient connection to current employment; not a Commonwealth supported place fee paid upfront or via HECS-HELP; not a HELP/SFSS/SSL/AASL/VSL repayment; not reimbursed | Travel, car and decline-in-value self-education costs |
| `WorkClothing` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/clothes-and-items-you-wear-at-work/clothing-laundry-and-dry-cleaning-expenses)) | D3 C | Claim type C, N, S or P | Protective, occupation-specific, distinctive compulsory or registered non-compulsory uniform; not plain clothing | Per-load laundry rates |
| `ToolsEquipment` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/tools-and-equipment-to-perform-your-work)) | D5 E | Amount ≤ $300; work use > 50% | Not part of a set or identical items costing more than $300 together | Decline in value of dearer items |
| `SoftwareSubscriptions` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/computers-laptops-and-software)) | D5 E | Amount ≤ $300; work use > 50% | As above; includes subscriptions, reference books and journals ([books guidance](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/books-periodicals-and-digital-information)) | Decline in value of dearer items |
| `PhoneInternet` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/mobile-phone-mobile-internet-and-other-devices)) | D5 E | — | Bills only; work use from records (e.g. a 4-week diary); not using the working-from-home fixed-rate method, which already includes phone and data | Handsets and devices |
| `Donations` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/gifts-and-donations)) | D9 J | Whole amount (100%, no reimbursement); at least $2, otherwise excluded | Recipient is a deductible gift recipient (DGR); nothing received in return | Property, shares, fundraising events, political gifts |
| `TaxAffairs` ([ATO](https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/cost-of-managing-tax-affairs)) | D10 M | — | Registered tax agent, recognised tax adviser, or tax material/software (apportioned); not penalties or general financial advice | Litigation costs (label L) |

## Entry figures

- Approved claims are grouped by myTax label.
- Each label adds up its approved cents and then omits them once.
- The overall entry figure is the sum of the label figures.
- For labels with a CLAIM TYPE box (D3, D4), the code with the largest approved amount is shown as "the main type". This follows the instructions' wording. Every row's own code is kept in the workbook.
- Packs frozen before this change remain a single D5 E entry.

## Out of scope

The standard deduction (2026–27 onwards) is not covered, and neither is any year without its own reviewed rule. Rules never roll forward.
