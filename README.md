# AI-Framework

Independent .NET AI libraries for document ingestion, scoped retrieval and source-grounded answers, with Local, Azure and AWS adapters. No dependency on Framework, MixPilot, BusinessSuite, ASP.NET Core in the core libraries, or a business-domain database.

Structured generation is also available through a separate provider-neutral interface and
an opt-in Azure adapter. It does not require retrieval/embeddings. See
[structured generation](docs/STRUCTURED_GENERATION.md) for schema limits, registration and
the distinction from existing Bedrock document-answer support.

## Libraries

| Project | Responsibility |
| --- | --- |
| `AIFramework.Core` | Document, chunk, profile and generation models; indexing, retrieval, model, embedding and extraction contracts; portable text chunker. No NuGet dependencies. |
| `AIFramework.Contracts` | Validated requests and response/citation DTOs; no Framework or HTTP host dependency. |
| `AIFramework.Application` | Ingestion and answer orchestration, default document profiles and explicit registration. |
| `AIFramework.Local` | Ephemeral in-memory retrieval and deterministic extractive answers for development/tests. |
| `AIFramework.Azure` | Azure OpenAI chat/embeddings and Azure AI Search indexing/retrieval. |
| `AIFramework.Aws` | Amazon Bedrock Converse text generation; independent of Azure and retrieval provider. |
| `AIFramework.LocalDemo` | Standalone console example with synthetic documents and scope checks; not a production library. |
| `AIFramework.SampleApi` | Standalone example host, not a shared production API or identity service. |

## Build and test

The .NET 10 SDK builds net9.0 and net10.0; the .NET 9 SDK builds net9.0 only. From this repository:
Running tests for both targets requires both the .NET 9 and .NET 10 runtimes.

```powershell
dotnet restore AI-Framework.sln
dotnet build AI-Framework.sln --no-restore -c Release
dotnet test AI-Framework.sln --no-build --no-restore -c Release
dotnet pack AI-Framework.sln --no-build --no-restore -c Release -o .artifacts/packages
```

All six library projects are packable; sample/tests are not. Package version starts at `0.1.0`; override with `-p:Version=...` for an approved release. Publish packages only through your selected private feed. No license is assigned by the scaffold; choose licensing before distributing. Package versions are owned by this repository, never imported from Framework. The libraries target net9.0 and net10.0; removing net9.0 is a separate coordinated decision.

## Use without a web server or database

Run the complete synthetic document example:

```powershell
dotnet run --project samples/AIFramework.LocalDemo --framework net9.0
```

It demonstrates ingestion, scoped retrieval, source citations and denied-evidence
checks using only the existing Application and Local libraries. See
[getting started and storage ownership](docs/GETTING_STARTED.md). No SQL tables or
production seed values are required; sample documents are loaded only by the demo.

Reference `AIFramework.Application` and `AIFramework.Local` and register:

```csharp
services.AddAiApplication();
services.AddLocalAi(); // Development/testing: process-local data, no actual LLM.
```

Resolve `DocumentIngestionService` to ingest plain text with a tenant ID and document profile. Resolve `KnowledgeAnswerService` with the authorized tenant/scope/document IDs to ask questions. No cloud account is required for Local. Local loses all indexed documents on restart.

To preserve existing full Azure configuration:

```csharp
services.AddAiApplication();
services.AddAzureAi(configuration);
```

The configuration section is `AI:Azure`, with `OpenAiEndpoint`, `ChatDeployment`, `EmbeddingDeployment`, `SearchEndpoint` and `SearchIndex`. All endpoints must be HTTPS. Credentials use `DefaultAzureCredential`; override the registered `TokenCredential` when the host has a different identity strategy. Runtime HTTP clients use `IHttpClientFactory` with a 60-second timeout; no automatic ingestion retry policy is implied.

Model generation and retrieval can also be selected independently:

```csharp
services.AddAzureModels(new AzureModelOptions(openAiEndpoint, chatDeployment, embeddingDeployment));
services.AddAzureSearch(new AzureSearchOptions(searchEndpoint, indexName));
services.AddBedrockChat(new BedrockOptions("eu-central-1", permittedModelOrInferenceProfileId));
```

Here embeddings/search use Azure and chat uses Bedrock. Search consumes `ITextEmbeddingModel`; its output dimensions must match the index. The last `IChatModel` registration selects chat. Register exactly one intended index/retriever/model per service collection; custom application-owned retrievers can replace the default. AWS uses the SDK credential chain; production should use workload roles. Bedrock requires a supported Converse model, regional access and scoped `bedrock:InvokeModel` permissions. Supply a model/inference-profile ID explicitly; no model is silently selected.

## Sample API

```powershell
$env:AI__AllowAnonymousDevelopment = 'true'
dotnet run --project samples/AIFramework.SampleApi --launch-profile http --framework net9.0
```

Swagger is at `http://localhost:5290/swagger`. Anonymous access requires that explicit setting **and** Development. Otherwise signed access tokens are required; the sample checks application `tenant_id` and optional `scope_id` claims. Outside Development configure `Authentication__Authority` and `Authentication__Audience`. Production use of the sample's Local/Aws ephemeral retrieval is rejected. Do not expose an anonymous demo publicly.

Routes preserve the extracted document workflow:

- `GET /api/v1/ai/document-profiles`
- `POST /api/v1/knowledge/documents`
- `POST /api/v1/ai/answers`
- `GET /health/live`

Requests/responses retain their JSON shapes; the sample owns its success envelope and Problem Details errors. The host needs no relational database. It is an integration example, not a production document system.

## Product boundaries and safety

Read [architecture and integration](docs/ARCHITECTURE.md). The host authenticates the user and resolves authorized tenant/resource scope before calling these libraries. Tenant IDs supplied to a library are not proof of authorization. Retrieval filters and a second check before generation enforce the supplied boundaries; they do not establish memberships.

`Grounded=true` means evidence was retrieved and supplied to generation, not that each generated assertion was independently verified. Citations identify retrieved evidence, not necessarily every passage used by the model. `RequiresHumanReview` labels a profile and adds a notice; the consuming application must enforce actual approval. Uploaded text is untrusted; prompt instructions are only one layer of defense. No autonomous business tools, exports, filings or financial postings are executed.

Ingestion currently accepts text and completes synchronously with a one-million-character library limit. HTTP hosting imposes its own body limits. Durable document metadata, idempotency, file upload/scanning, OCR implementations, deletion/reindex workflows, queues/outbox, usage budgets and telemetry exporters remain extensions. AWS retrieval/embeddings and Bedrock structured generation are not implemented. The separate structured-generation interface has an Azure implementation; product extraction schemas remain application-owned. `IDocumentTextExtractor` is an extension contract, not a working OCR service.

Cloud adapters are tested with simulated clients/HTTP responses. Live cloud access, costs, regional compatibility, deployment plans and real document evaluations require environment-specific verification.

## Consume from other repositories

During workspace development use sibling project references with an overridable `AIFrameworkRoot`. For independent releases use the private NuGet packages with matching versions. Both modes must preserve the same dependency graph; do not consume the same assembly through project and package references simultaneously.

MixPilot consumes the application/contracts and selected adapters. BusinessSuite retains an explicit Core reference for future integration and does not gain financial AI endpoints from this extraction. Neither AIFramework nor the general Framework references either product.

ConstructionFlow composes these libraries from its Application workflows and supplies a product-owned durable PostgreSQL index/retriever. Authorization, human approval and audit remain in ConstructionFlow; no construction-specific approval or database code belongs in AI-Framework. The general Framework's optional persistence/mapping split does not change these AI contracts. Keep project references for workspace development and introduce private package references only when independently releasing a tested product.
