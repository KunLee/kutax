# Receipt import and processing

Use bounded raw-body uploads (20 MiB, PDF/JPEG/PNG signatures) with percent-encoded metadata headers, SHA-256 deduplication per owner and opaque private S3 keys. PostgreSQL commits document metadata, import outcome and processing job together. Retries reuse a client item ID. Failed persistence removes the candidate object; recovery tooling will check orphaned objects.

Process jobs in the application using PostgreSQL `FOR UPDATE SKIP LOCKED`, a three-minute lease and a unique lease token. Work is bounded to 90 seconds, retries are bounded to three, and expired leases can be reclaimed after process failure. Persist extraction attempts separately from human-confirmed facts. Retrying extraction never replaces an existing transaction.

Default extraction is manual and makes no invented suggestions. The optional synthetic fixture parser is Development-only and is not OCR. The optional real adapter uses the documented OpenAI Responses API with direct PDF/image input, strict JSON Schema, no tools and `store:false`. An operator must configure a model, endpoint, region and retention notice; each user must grant revocable consent. No external processing is exercised without credentials and consent. Provider policies still apply; `store:false` is not a guarantee of zero retention.

Sources reviewed 10 October 2026: [file inputs](https://developers.openai.com/api/docs/guides/file-inputs), [structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [data controls](https://developers.openai.com/api/docs/guides/your-data). Content is untrusted input; schema and semantic validation reject missing provenance, invalid confidence, currency, dates or amounts. Raw prompts and provider responses are not logged.

Human corrections use optimistic concurrency and append a before/after audit event. Allocations retain work-use and work-reimbursement separately and are bounded by the invoice amount. Fact edits invalidate all claim approvals. Replacing allocations creates new IDs; older immutable report snapshots retain their historical rows and audit references.
