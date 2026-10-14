# 0011 — Bilingual interface and tax packs (English / Simplified Chinese)

Status: accepted, 12 October 2026

## Context

The owner wants to use KuTax in Chinese and switch freely, and wants exported files available in both languages. myTax and the ATO are English-only, so whatever the interface language, the person must still be able to match KuTax figures to English myTax screens.

## Decision

- **English text is the key.** Templates keep English strings and pass them through an impure `t` pipe (`core/i18n.ts`). A missing translation shows English, never a blank. Server messages are translated the same way: exact matches first, then regex patterns whose captured parts (a rule name, a nested message) are translated recursively.
- **No Angular `@angular/localize` build-per-locale.** Runtime switching without reload was a requirement, and one production image serves the SPA.
- **The Chinese dictionary loads only when used** (separate ~26 kB gzipped chunk). A saved Chinese choice preloads it before bootstrap, so the first paint is already Chinese. Choice is stored per device (`kutax.language`), defaulting to the browser language.
- **myTax section names stay English** in both languages, and myTax button names are shown in English with a Chinese gloss (for example “Prepare tax return（准备报税表）”). Merchant names, descriptions and notes are the person's data and are not translated.
- **Tax packs are generated in both languages every time**: `summary`/`guide`/`workbook` and `summary-zh`/`guide-zh`/`workbook-zh`, plus one evidence ZIP. Figures are identical; only labels differ. The download panel follows the interface language and offers an English / 中文 toggle; packs prepared before this change only have English and show no toggle.
- **Chinese PDFs embed a Noto Sans SC subset** and insert zero-width break opportunities after CJK characters (PDF only) so MigraDoc wraps Chinese lines. Excel sheet names and PDF metadata use plain text.

## Consequences

- New UI strings must be added to `i18n-zh.ts`; `i18n_keys.py`-style extraction (in development notes) lists keys without a translation. Untranslated strings degrade to English.
- Packs grow by three files (the Chinese PDFs are larger because of the embedded font subset).
- Translations are a reading aid; the deterministic figures and rule evaluation are language-independent.
