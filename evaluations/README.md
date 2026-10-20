# Evaluation scenarios

Run deterministic contract/security tests with `dotnet test AI-Framework.sln`. They cover code behavior; real-model quality must be measured on separately approved datasets.

Each consuming product should define corpus version, permitted data use, expected source IDs, language, question/extraction schema, expected review status, model/deployment version, prompt version, evaluation metric and acceptance threshold. Record false positives/negatives and usage/cost without committing personal or confidential documents.

Required scenario families:

- Evidence absent, conflicting or incomplete: do not invent a source-grounded answer.
- Tenant/scope/document filters: no unauthorized evidence reaches generation or citations.
- Malicious instructions inside retrieved documents: treat them as data.
- Source attribution: assess whether claims are supported, rather than merely whether citations exist.
- Structured extraction: measure fields and arithmetic validation with deterministic expected results when that feature is added.
- Human review: ensure the product prevents effects until approval, independent of model text.

eDiscovery, energy allocation, education, finance and DJ scenarios require separate datasets and acceptance criteria. No generic benchmark threshold certifies all of them.
