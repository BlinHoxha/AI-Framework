# Structured generation

Added 4 November 2026 for a reviewed chart-description workflow in AstroIllustrator.
The shared libraries contain no astronomy/product proposal types.

`IStructuredGenerationModel` is separate from evidence-based `IChatModel`. A request supplies
system instructions, untrusted input, a schema name, a closed JSON schema and output-token cap.
Results distinguish Completed, Refused and Incomplete; only Completed includes JSON.
Provider/token-count metadata does not imply semantic correctness or monetary cost accounting.
Azure structured results also include the response `Model` when the provider reports it.

The initial adapter is `AzureStructuredGenerationModel`, registered using
`services.AddAzureStructuredGeneration(new AzureStructuredOptions(endpoint, deployment))`.
It reuses `AzureTokenClient` and the existing DefaultAzureCredential transport without
registering embeddings, search, document profiles or a knowledge-answer service. The
caller explicitly chooses an HTTPS resource root and a deployment compatible with strict
JSON-schema output and max_completion_tokens. No model, credential or live call is supplied.

The adapter sends `response_format: json_schema` with `strict: true` to API version
2024-10-21. It bounds response JSON at 128 KiB, checks refusal/finish_reason before reading
content, and independently validates the small schema subset. It never executes tool calls
or returns partial output. Existing Azure document-generation/embedding transport behavior
is preserved; the bounded streaming path is opt-in for this adapter.

Supported local schema validation: type (including nullable type arrays), closed objects,
all properties required, arrays/items, enums and descriptions. Other keywords are rejected
before calling Azure. This is not a full JSON Schema validator; product rules still need
application validation. Root object schemas are intended for Azure strict generation.
No automatic retry, fallback, durable quota or product approval is provided by this library.
The consuming host owns cancellation/deadlines, user authorization, review, privacy and budgets.

Tests use simulated HTTP responses/credentials, not billable Azure requests. They cover
schema transport, invalid/extra/duplicate fields, refusal, incomplete output, bounds and
cancellation. Live deployment compatibility and semantic evaluations remain consumer tasks.
Reference: [Microsoft structured outputs](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/structured-outputs).

Bedrock can later implement this same interface while its existing grounded-answer adapter
continues unchanged. Verify equivalent structured-output guarantees for the chosen model/API;
do not relabel evidence-answer text as validated structured output or silently retry elsewhere.
