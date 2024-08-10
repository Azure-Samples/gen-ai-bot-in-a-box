param location string

// Dependencies
param aiServicesName string
param storageName string
param keyVaultName string
param searchName string = ''

// Azure AI configuration
param aiHubName string
param aiProjectName string

// Other
param tags object = {}
param publicNetworkAccess string
param systemDatastoresAuthMode string
param privateEndpointSubnetId string
param apiPrivateDnsZoneId string
param notebookPrivateDnsZoneId string
param defaultComputeName string
param deployAIProject bool

resource aiServices 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: aiServicesName
}
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}
resource search 'Microsoft.Search/searchServices@2023-11-01' existing = {
  name: searchName
}

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-04-01-preview' = {
  name: aiHubName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publicNetworkAccess: publicNetworkAccess
    managedNetwork: {
      isolationMode: publicNetworkAccess == 'Disabled' ? 'AllowOnlyApprovedOutbound' : 'Disabled'
      outboundRules: publicNetworkAccess == 'Disabled' && !empty(search.name)
        ? {
            'rule-${search.name}': {
              type: 'PrivateEndpoint'
              destination: {
                serviceResourceId: search.id
                subresourceTarget: 'searchService'
              }
            }
          }
        : {}
    }
    friendlyName: aiHubName
    keyVault: keyVault.id
    storageAccount: storage.id
    systemDatastoresAuthMode: systemDatastoresAuthMode
  }
  kind: 'hub'

  resource aiServicesConnection 'connections@2024-04-01' = {
    name: '${aiHubName}-connection-AIServices'
    properties: {
      category: 'AIServices'
      target: aiServices.properties.endpoint
      authType: 'AAD'
      isSharedToAll: true
      metadata: {
        ApiType: 'Azure'
        ResourceId: aiServices.id
        ApiVersion: '2023-07-01-preview'
        DeploymentApiVersion: '2023-10-01-preview'
        Location: location
      }
    }
  }

  resource searchConnection 'connections@2024-04-01' = if (!empty(searchName)) {
    name: '${aiHubName}-connection-Search'
    properties: {
      category: 'CognitiveSearch'
      target: 'https://${search.name}.search.windows.net'
      authType: 'AAD'
      isSharedToAll: true
    }
  }

  resource defaultCompute 'computes@2024-04-01-preview' = {
    name: defaultComputeName
    location: location
    tags: tags
    properties: {
      computeType: 'ComputeInstance'
      properties: {
        vmSize: 'Standard_DS11_v2'
      }
    }
  }
}

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-04-01-preview' = if (deployAIProject) {
  name: aiProjectName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publicNetworkAccess: publicNetworkAccess
    hubResourceId: aiHub.id
  }
  kind: 'Project'
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
  name: 'pl-${aiHubName}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'private-endpoint-connection'
        properties: {
          privateLinkServiceId: aiHub.id
          groupIds: ['amlworkspace']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${aiHubName}'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'api'
          properties: {
            privateDnsZoneId: apiPrivateDnsZoneId
          }
        }
        {
          name: 'notebook'
          properties: {
            privateDnsZoneId: notebookPrivateDnsZoneId
          }
        }
      ]
    }
  }
}

output aiHubID string = aiHub.id
output aiHubName string = aiHub.name
output aiProjectID string = aiProject.id
output aiProjectName string = aiProject.name
