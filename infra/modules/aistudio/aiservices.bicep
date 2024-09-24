param location string
param aiServicesName string
param tags object = {}
param privateEndpointSubnetId string
param publicNetworkAccess string
param openAIPrivateDnsZoneId string
param cognitiveServicesPrivateDnsZoneId string
param grantAccessTo array
param allowedIpAddresses array = []
param authMode string

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
    disableLocalAuth: authMode == 'accessKey' ? false : true
    customSubDomainName: aiServicesName
    publicNetworkAccess: !empty(allowedIpAddresses) ? 'Enabled' : publicNetworkAccess
    networkAcls: {
      defaultAction: publicNetworkAccess == 'Enabled' ? 'Allow' : 'Deny'
      ipRules: [
        for ipAddress in allowedIpAddresses: {
          value: ipAddress
        }
      ]
    }
  }
  tags: tags
}

resource aiPrivateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
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

resource cognitiveServicesPrivateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
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

resource cognitiveServicesOpenAIContributor 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'a001fd3d-188f-4b5d-821b-7da978bf7442'
}

resource cognitiveServicesUser 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'a97b65f3-24c7-4388-baec-2e87135dc908'
}

resource openaiAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in grantAccessTo: if (!empty(principal.id)) {
    name: guid(principal.id, aiServices.id, cognitiveServicesOpenAIContributor.id)
    scope: aiServices
    properties: {
      roleDefinitionId: cognitiveServicesOpenAIContributor.id
      principalId: principal.id
      principalType: principal.type
    }
  }
]

resource userAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in grantAccessTo: if (!empty(principal.id)) {
    name: guid(principal.id, aiServices.id, cognitiveServicesUser.id)
    scope: aiServices
    properties: {
      roleDefinitionId: cognitiveServicesUser.id
      principalId: principal.id
      principalType: principal.type
    }
  }
]

output aiServicesID string = aiServices.id
output aiServicesName string = aiServices.name
output aiServicesEndpoint string = aiServices.properties.endpoint
output aiServicesPrincipalId string = aiServices.identity.principalId
