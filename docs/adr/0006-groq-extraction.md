# Groq receipt extraction

Groq is an optional extraction provider beside the existing OpenAI Responses adapter. Select it with `Ai:Provider=Groq`. It uses Groq's OpenAI-compatible chat completions endpoint with `response_format` strict JSON Schema (the same receipt schema), `temperature 0` and no tools. KuTax's own schema and semantic validation still reject anything untraceable or invalid, and every value still needs human confirmation.

Facts from Groq's documentation, checked 10 October 2026:
- **Models:** `qwen/qwen3.8-27b` is the only model listed for image input, and it supports strict structured outputs. It is a **preview** model that Groq may discontinue at short notice. `openai/gpt-oss-120b` is a production model with strict structured outputs.
- **Inputs:** Groq has no PDF input. A request containing an image may be up to 20 MB, with base64 data URLs supported.
- **Data:** By default Groq does not retain inference data, but may log inputs and outputs for up to 30 days for reliability or abuse investigation. Zero Data Retention can be enabled in Data Controls. Customer data is stored in Google Cloud in the United States.

Design:
- Photos (JPEG/PNG up to 14 MB, which keeps base64 under Groq's limit) go to `Ai:Model`, the vision model.
- PDFs: PdfPig 0.1.16 extracts the text layer of up to 5 pages and 20,000 characters, which is sent to `Ai:TextModel` between explicit untrusted-data markers.
- Scanned PDFs with no text layer, and oversized images, fall back to manual entry without calling Groq.
- Groq configuration requires `Ai:TextModel`. The text model is part of the privacy notice version, so changing it requires fresh user consent; rotating the API key does not.
- Raw prompts and responses are not logged. Responses are size-bounded.

Prerequisites before using real receipts:
- the operator records the region and retention notice;
- Zero Data Retention is enabled;
- each user consents in Settings;
- accuracy is measured with `--evaluate-synthetic` on the 100 synthetic receipts.

Update, 11 October 2026:
- **Settings in the app.** Administrators manage AI settings in **Settings → AI provider**. They are stored in a single `AiSettings` row with the API key encrypted by ASP.NET data protection (purpose `KuTax.AiSettings.ApiKey.v1`). The key is write-only, and the UI only ever shows its last 4 characters. `infra/.env` remains the fallback until the first save. The endpoint stays server-only, so an administrator cannot point the key at another host. **Check key & load models** lists the models from the provider's `/models` endpoint using the saved key.
- **Rate limits.** HTTP 429 raises `ProviderRateLimitedException`, which carries the provider's `Retry-After`. The worker pauses the queue and requeues the receipt without using up an attempt. A wait over 10 minutes (a spent daily quota) hands the receipt to manual entry.
- **Accuracy.** The first live evaluation of 100 synthetic receipts scored merchant 100%, amount 100%, date 86%. Every date miss was an Australian day-first numeric date, so the instructions now say receipts are Australian and dates are day-first. The re-run scored 100% / 100% / 100% with 0 failures. That covers synthetic text PDFs only; photo accuracy is unmeasured.

Update, 11 October 2026 (scans): PDFs without a text layer now send their largest page images (up to 3, from the first 2 pages, at least 300 px on each side) to the vision model. JPEG streams are sent unchanged; other image formats are converted to PNG. PDFs with neither text nor usable images still fall back to manual entry. A live evaluation of 20 synthetic photos and 20 synthetic scanned PDFs scored 100% for merchant, date and amount.

If Groq withdraws the preview vision model, configure a replacement vision model. Changing the model invalidates existing consents, so every user must consent again.
