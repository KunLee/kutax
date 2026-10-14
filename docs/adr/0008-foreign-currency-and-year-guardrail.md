# Foreign-currency receipts and the financial-year guardrail

## Foreign currency

The ATO translates a foreign-currency expense at the exchange rate when it was incurred or paid, not at today's rate. It has used Reserve Bank of Australia rates since 1 January 2020. The AUD amount actually charged on the person's statement is the best evidence. KuTax never converts silently.

- **AI extraction.**
  - Amounts are filled only when they are in AUD.
  - For other currencies the AI returns `originalCurrency` (ISO code) and `originalAmount` as separate, validated fields.
  - Invalid or untraceable values are blanked; an amount without a valid currency is dropped.
- **Transaction fields.** A transaction stores `OriginalCurrency`, `OriginalAmount` and `ConversionSource`. The source is either `Statement` (the AUD amount entered from a bank or card statement, the recommended option) or `RBA` (an estimate). An RBA estimate also stores the rate used (`ConversionRate`, units per A$1 as published) and its date.
- **RBA rates.**
  - Source: RBA statistical table F11.1 (daily, 3 January 2023 onwards, 21 currencies), stored in `ExchangeRates`.
  - The CSV is parsed by its "Units" row, the download is size-capped, and it refreshes at most hourly, only when the stored rates don't reach the requested date.
  - Weekends and holidays use the latest earlier business day, up to 7 days back. Dates before 2023 have no rate; people use their statement amount.
  - Only public rate data is fetched.
- **Server-side check.** `GET /api/exchange-rates/{currency}/{date}?amount=` returns the rate and the server-computed AUD figure. Saving with source `RBA` recomputes it and rejects any mismatch, so a confirmed estimate always equals the published rate. Statement amounts are taken as entered.
- **Trail in outputs.** Claim rows, snapshots, the Expenses worksheet (appended columns, so existing positions are unchanged), the claim views and History show the original amount and how its AUD figure was obtained.

## Financial-year guardrail

Tax packs only include receipts dated inside their own financial year. The guardrail makes this visible:

- **On the receipt.** When the date falls in another year, a warning names that year. It offers **Switch to FY…** or **Skip this receipt**, which marks it not claimed so it is excluded from every year's pack and exceptions. Confirming keeps it for the year of its date.
- **In the queue.** The list labels receipts dated in another year.
