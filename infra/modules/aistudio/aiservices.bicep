param location string
param aiServicesName string
param tags object = {}
param privateEndpointSubnetId string
param publicNetworkAccess string
param openAIPrivateDnsZoneId string
param cognitiveServicesPrivateDnsZoneId string
param grantAccessTo array
param allowedIpAddresses array = []

resource aiServices 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: aiServicesName
  location: location
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  kind: 'AIServices'
  properties: {
    disableLocalAuth: true
    customSubDomainName: aiServicesName
    publicNetworkAccess: !empty(allowedIpAddresses) ? 'Enabled' : publicNetworkAccess
    networkAcls: {
      defaultAction: 'Deny'
      ipRules: [
        for ipAddress in allowedIpAddresses: {
          value: ipAddress
        }
      ]
    }
  }
  tags: tags
}

resource aiPrivateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = {
  name: 'pl-oai-${aiServicesName}'
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
          privateLinkServiceId: aiServices.id
          groupIds: ['account']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${aiServicesName}'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'default'
          properties: {
            privateDnsZoneId: openAIPrivateDnsZoneId
          }
        }
      ]
    }
  }
}

resource cognitiveServicesPrivateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = {
  name: 'pl-${aiServicesName}'
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
          privateLinkServiceId: aiServices.id
          groupIds: ['account']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${aiServicesName}'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'default'
          properties: {
            privateDnsZoneId: cognitiveServicesPrivateDnsZoneId
          }
        }
      ]
    }
  }
}

resource cognitiveServicesContributor 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68'
}

resource writerAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in grantAccessTo: if (!empty(principal.id)) {
    name: guid(principal.id, aiServices.id, cognitiveServicesContributor.id)
    scope: aiServices
    properties: {
      roleDefinitionId: cognitiveServicesContributor.id
      principalId: principal.id
      principalType: principal.type
    }
  }
]

output aiServicesID string = aiServices.id
output aiServicesName string = aiServices.name
output aiServicesEndpoint string = aiServices.properties.endpoint
output aiServicesPrincipalId string = aiServices.identity.principalId
