targetScope = 'resourceGroup'
param prefix string
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: '${prefix}-api-identity' }
resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = { name: '${prefix}-openai' }
resource search 'Microsoft.Search/searchServices@2025-05-01' existing = { name: '${prefix}-search' }
resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, identity.id, 'openai-user')
  scope: openAi
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
  }
}

resource searchContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, identity.id, 'search-data-contributor')
  scope: search
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
  }
}

resource searchReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, identity.id, 'search-data-reader')
  scope: search
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '1407120a-92aa-4202-b7e9-c0e197c71c8f')
  }
}