# Local Ollama generation

`AIFramework.Ollama` supplies `ITextGenerationModel` and `IStructuredGenerationModel` through
Ollama's native `/api/chat` endpoint. It is for **Development only**. The host chooses the
provider explicitly; this library does not route requests or configure an environment.
Ollama has no authentication. Keep the server bound to `127.0.0.1`; never set
`OLLAMA_HOST=0.0.0.0`. Keep `OLLAMA_DEBUG` off so prompts do not enter verbose logs. The
adapter accepts only loopback endpoint roots and does not follow redirects when registered
through `AddOllamaModels`.

Install and select a model manually before running an app. The library never downloads,
creates, deletes or publishes models. Record the exact digest with the deployment notes. The
development baseline tested here is `qwen3:1.7b`, digest
`8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7` on local
Ollama 0.40.16. The library makes no `/v1/` calls.

```csharp
services.AddOllamaModels(new OllamaOptions(
    new Uri("http://127.0.0.1:11434/"), "qwen3:1.7b"));
```

`OllamaOptions` requires an explicit model. `ContextTokens` defaults to 8192 and is sent as
`options.num_ctx`; `TimeoutSeconds` defaults to 120. `Think` defaults to `false`, because
Qwen3 can spend a short output budget on hidden reasoning and return empty visible content.
Set it to `null` to omit the field. The adapter sends `stream:false` and a caller-selected
`num_predict` output cap. It checks
`EstimateInputTokensUpperBound(instructions, input) + MaxOutputTokens` before making a call.
This is a conservative upper estimate based on UTF-8 bytes, so it can reject requests that
would fit the model. The check avoids sending obviously oversized input; it does not promise
that the model will never truncate context. The raw 512-token context probe with about 3,000
English words is recorded below.

Structured generation sends the caller's closed object schema as native `format` and uses
`temperature:0`. Ollama's grammar-constrained output is not Azure strict mode. The adapter
parses the result and checks `StructuredJson.Matches` before returning `Completed`; consumers
must also validate domain rules. A length stop returns `Incomplete` without partial JSON.

The model can be metered at zero monetary price with a host-owned price catalog:

```csharp
new AiPrice(new("ollama", "qwen3:1.7b"), 0, 0, "USD", "local-2026-11")
```

Token counts come from `prompt_eval_count` and `eval_count` when Ollama reports non-negative
Int32 values. They do not include machine or electricity cost. This adapter offers no
streaming, embeddings, grounded chat, retries or paid-provider fallback.

## Local verification on 2026-11-13

With `AIFRAMEWORK_OLLAMA_LIVE_MODEL=qwen3:1.7b`, all four opt-in live tests passed on
the local server. Single CPU-only samples: plain text 10.0 s (29 prompt, 10 output tokens)
and structured generation 2.6 s (41 prompt, 16 output tokens). These are individual
measurements, not performance promises.

The raw probe bypassed the adapter's context check and sent about 3,000 English words with
`num_ctx:512` and `num_predict:16`. Ollama returned HTTP 200 with
`prompt_eval_count:258` in 10.4 s, rather than rejecting the call. This is consistent with
context truncation; the response does not identify which input words were retained.
