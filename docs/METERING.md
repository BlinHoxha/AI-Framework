# AI metering foundation

AI-Framework provides opt-in metered text and structured generation. The host passes an
authenticated `AiUsageScope` (product, environment, tenant key, feature) and a unique request
key on every call. The framework does not infer authorization or trust an ambient tenant.
`AiModelKey` selects the host-configured price; the provider's response model is recorded
separately because a deployment can report a different underlying model.

Register an `AiPriceCatalog`, a durable `IAiBudgetLedger`, and the relevant generation model,
then call `AddAiMetering(catalog, options => { options.TextModel = key; options.StructuredModel = key; })`.
The ledger must be registered by the host; metered services fail to resolve without one.
Products supply their own versioned prices. No prices are built into this library.

Each call validates its scope and request, finds a price, reserves the estimated maximum,
calls the provider, and commits an estimated actual cost. A missing price, missing budget,
exhausted budget or duplicate request key blocks the provider call. The reserve operation is
atomic against every applicable limit. A pre-cancelled call releases its reservation; after
the provider call starts, success, failure and cancellation commit a usage record with an
outcome. Commit and release use an uncancelled token so caller cancellation cannot skip
accounting. A provider failure is conservatively charged at the reserved amount pending
product-owned reconciliation. Missing or invalid token usage also charges the reservation.

`AiCostCalculator` uses exact decimal arithmetic without rounding. Its input token upper
bound is the UTF-8 byte count of each input text plus 32 tokens per text. This assumes a
byte-level tokenizer uses at most one token per byte; model-specific tokenization and
provider overhead can differ. The configured output-token cap completes the maximum-cost
estimate. Costs are estimates from token counts and configured prices, not provider-reported
charges. A monthly budget is an operating target, not a hard billing cap.

`AIFramework.Metering` activities and metrics expose only product, environment, feature,
provider, model, outcome and denial code. They never contain tenant keys, request keys,
prompts, inputs or outputs. Counters are `ai.requests`, `ai.tokens.input`,
`ai.tokens.output` and `ai.cost.estimated`.

`AddInMemoryAiBudgetLedger` is for development and tests only. Its limits apply per UTC
calendar month and can cover a product/environment and optional tenant. A reservation must
fit all matching limits. Committed cost replaces the reservation in its original month;
release frees it. State is process-local and resets on restart. A production ledger must
persist reservations, enforce atomic idempotency and all applicable limits across processes,
retain the reservation period, and reconcile estimated costs with provider billing.
