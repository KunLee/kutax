# Periodic employment fees — reviewed implementation basis

Verified against official sources on 10 October 2026. Coverage: FY 2024–25 and FY 2025–26 only. Each category/year has a separate immutable `AU-{startYear}-{category}-v1` definition. An administrator must record an explicit source review in KuTax before an owner may approve claims. This document records engineering verification, not a professional tax opinion.

The supported subset is ordinary periodic union or professional association membership fees connected to current employment. The owner must confirm the fee was both paid and incurred within the selected year and covers services entirely within that year. This conservative restriction excludes otherwise potentially deductible cases requiring different timing analysis. Joining fees, levies, capital expenditure, unrelated memberships, prepayments crossing years, and special section 25-55 treatment are unsupported; do not infer that all such expenses are legally non-deductible.

Eligibility sources: [ATO TR 2000/7](https://www.ato.gov.au/law/view/print?DocID=TXR%2FTR20007%2FNAT%2FATO%2F00001), [union and association fee guidance](https://www.ato.gov.au/api/public/content/0-8fc4856d-7fa3-4468-8d84-fd75defbd782), and [myTax 2025 deductions](https://www.ato.gov.au/myTax25Deductions). These establish the employment connection, private/reimbursed exclusions and GST-inclusive deduction basis. No GST input credit is calculated for this PAYG-only subset.

Year-specific mapping was checked in the official individual tax return instructions, question D5, “Other work-related expenses”, including union and professional association fees:

| FY | Official publication | SHA-256 of downloaded source |
|---|---|---|
| 2024–25 | [Individual tax return instructions 2025](https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y) | `C4B8FA8BB372485CC8E66FF38CC62D1CF6FA2B12834380FDC88EEC3F6EE946CB` |
| 2025–26 | [Individual tax return instructions 2026](https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y) | `5EFF5D035035E7B0898B8B316A6CC1D6B616577E10157A9D97B1B5CD30A68BF5` |

The [official 2026 paper return](https://iorder.com.au/publication/Download.aspx?ProdID=2541-6.2026&dl=Y), SHA-256 `7C8C912C61B0AAE6562D89524CC6B4E3744AE806B50D0BBF7CDB5711342019E9`, shows whole-dollar D5 label E. “D5 E” is a **paper reference**, never an API/import code. The online assistant uses the human-readable category label. Users must verify the current official myTax screen and reconcile existing prefill and other expenses in that section.

Calculation: decimal expense amount including observed GST × work-use percentage; round the work portion to two decimals, midpoint away from zero; subtract the documented reimbursement attributable to that work portion. No negative claim. A zero result is excluded. Category entry aggregates approved cents first, then omits cents once; retain exact approved totals and the omitted-cents reconciliation visibly. No rates, thresholds, liability or refund estimation are implemented.

Written original evidence is mandatory; additional evidence can be linked to any allocation. A rule review does not approve a claim. Each owner approval records allocation and transaction versions, the exact rule version, amount, evidence IDs/hashes, actor and timestamp. Fact, allocation or evidence changes invalidate current approval while retaining historical decisions. Unsupported years (including FY 2026–27) remain unassessed; rules never roll forward automatically.
