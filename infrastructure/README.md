# AI infrastructure ownership

AI resources live here; general API/container/database modules remain in Framework. Application deployments compose these modules and supply their own environment configuration. Choose one IaC owner for each resource set. This change does not deploy, import or destroy cloud resources.

## Azure

`azure/bicep/ai.bicep` creates Azure OpenAI chat/embedding deployments and Azure AI Search inside an existing resource group. `access.bicep` grants data-plane roles to an existing `${prefix}-api-identity`. The existing identity and AI prefix must match for this example; broader composition should accept explicit resource IDs.

`azure/terraform/` is an alternative child module with the same AI resource responsibilities. It takes an existing resource group, prefix, location, model versions/capacities, tags and optional runtime principal ID. The calling root configures the Azure provider and protected remote backend. No database or container is created by this module.

Deploying the module does not configure the consuming application. Supply `AI__Azure__OpenAiEndpoint`, `AI__Azure__ChatDeployment`, `AI__Azure__EmbeddingDeployment`, `AI__Azure__SearchEndpoint`, `AI__Azure__SearchIndex` and the managed-identity settings in that application's deployment.

Create the index with `azure/create-search-index.ps1` using an authorized deployment identity. The schema uses 1536-dimensional embeddings; configure a matching model or revise the index before use. Runtime identities should receive data-plane roles only. Resource templates retain the reference stack's public network configuration; production networking and private endpoints require a reviewed plan.

## Existing environments: migration is required

The former Framework templates managed AI resources in a combined stack. **Do not apply their removal to an existing environment before transferring resource ownership.** Terraform requires reviewed state moves/imports into the new module addresses; otherwise an apply can plan to destroy/recreate AI resources. For Bicep, review deployment mode, stacks and resource ownership, preserving existing names and identities. Back up state and indexed data and review a what-if/plan before any apply. Moving source files does not migrate cloud state.

## AWS

`aws/bedrock-access.yaml` creates an optional managed IAM policy scoped to supplied model/inference-profile resource ARNs. Attach it only to the intended workload role. The SDK uses workload credentials or the configured credential chain. Model access, supported Converse features, region and cross-region inference permissions must be verified for the chosen model. No AWS retrieval service, embeddings deployment or document extraction service is provisioned in this increment.

No cloud deployment or live provider call is part of the local verification. Validate templates and run environment-specific tests before provisioning.
