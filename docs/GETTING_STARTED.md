# First local document workflow

Start with the existing ingestion -> retrieval -> answer pipeline. No SQL database,
migrations, seeded users, cloud resources or agent runtime are required.

## Run the standalone example

From the AI-Framework repository with a .NET 10 SDK for net9.0 and net10.0, or a .NET 9 SDK for net9.0 only (net9.0 removal is a separate coordinated decision):

```powershell
dotnet run --project samples/AIFramework.LocalDemo --framework net9.0
```

The console example registers only Application and Local, loads two synthetic text
documents, and asks a question using a permitted tenant, scope and document ID.
It prints the evidence and source citation, then checks different-tenant,
different-scope, restricted-document, empty-allowlist and no-match requests.
Successful execution ends with `All checks passed`; a failed check exits with an error.
Document paths resolve relative to the built application, independently of the working directory.

The Local provider uses keyword retrieval and extractive answers, not an LLM or
semantic search. Its index is process-local and disappears on exit. The sample
explicitly ingests its fixtures; library registration and API startup do not seed data.
These checks demonstrate supplied scope filtering, not real authentication or memberships.

## Integrate into a product

Reference `AIFramework.Application` and the selected provider libraries, then register:

```csharp
services.AddAiApplication();
services.AddLocalAi(); // Development/tests only.
```

Resolve `DocumentIngestionService` and `KnowledgeAnswerService` from a DI scope.
The console's Program.cs provides complete request examples using the built-in
`general-document` profile. Register a custom `IDocumentProfileCatalog` only when
the product needs different profiles; profile defaults are code, not database rows.

The product must authenticate the caller and authorize every supplied boundary
before invoking these services. Never treat tenant/scope IDs from a request as
proof of access. `DocumentIds = []` permits no documents; `null` applies no extra
document-ID restriction within the supplied tenant/profile/scope. Scoped requests
also include tenant-wide documents whose ScopeId is null. Requests without a scope
retrieve only tenant-wide documents. Use explicit permitted document IDs or a
product-owned retriever when resource permissions are more granular.

`Grounded` means evidence was supplied, not that each claim was independently
verified. Citations identify retrieved chunks; they are not exact PDF page references.

## Storage ownership

| Data | Owner and starting point |
| --- | --- |
| Default profiles | AI-Framework code/configuration; no SQL seed required |
| Development chunks | Local provider's transient in-memory index |
| Persistent search chunks/vectors | Selected retrieval adapter; Azure AI Search uses its own index schema |
| Original files, document metadata, users, memberships and business records | Consuming product's storage and migrations |
| Processing jobs, conversations, approvals or usage records | Add product-owned persistence when a concrete feature needs it |

The example text files are development fixtures, not production records. Do not
put real documents or secrets in them. Original-file storage and a durable document
lifecycle must be designed by the product before production ingestion.

## Next provider and validation steps

For Azure, follow [the infrastructure guide](../infrastructure/README.md) to provision
the search index and model resources explicitly. Use the existing `AddAzureAi`
registration and README configuration. The embedding model's dimensions must match
the index. No resources are deployed by the local demo. AWS chat and alternative
retrieval composition are described in the root README.

```powershell
dotnet test AI-Framework.sln -c Release
```

Automated tests verify code and access boundaries. Before enabling real models,
define product-specific datasets and acceptance criteria using the
[evaluation guide](../evaluations/README.md). Multi-agent integration can be added
later when a concrete workflow needs it; it is not a prerequisite for document Q&A.
