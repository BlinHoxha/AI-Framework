targetScope = 'resourceGroup'
@minLength(5)
@maxLength(15)
param prefix string
param location string = resourceGroup().location
param tags object = {}
param chatModel string = 'gpt-4o-mini'
param chatModelVersion string
param embeddingModel string = 'text-embedding-3-small'
param embeddingModelVersion string
param chatCapacity int = 10
param embeddingCapacity int = 10
resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${prefix}-openai'
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: '${prefix}-openai'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }
  tags: tags
}

resource chat 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'chat'
  sku: { name: 'Standard', capacity: chatCapacity }
  properties: { model: { format: 'OpenAI', name: chatModel, version: chatModelVersion } }
}

resource embeddings 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'embeddings'
  sku: { name: 'Standard', capacity: embeddingCapacity }
  properties: { model: { format: 'OpenAI', name: embeddingModel, version: embeddingModelVersion } }
}

resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: '${prefix}-search'
  location: location
  sku: { name: 'basic' }
  properties: {
    disableLocalAuth: true
    authOptions: { aadOrApiKey: { aadAuthFailureMode: 'http401WithBearerChallenge' } }
    publicNetworkAccess: 'Enabled'
    replicaCount: 1
    partitionCount: 1
  }
  tags: tags
}
output openAiEndpoint string = 'https://${openAi.name}.openai.azure.com/'
output searchEndpoint string = 'https://${search.name}.search.windows.net/'
output searchName string = search.name