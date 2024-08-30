param location string
param searchName string
param tags object = {}
param publicNetworkAccess string
param privateEndpointSubnetId string
param privateDnsZoneId string
param allowedIpAddresses array = []

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: searchName
  location: location
  tags: tags
  sku: {
    name: 'standard'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    networkRuleSet: {
      bypass: 'AzureServices'
      ipRules: [
        for ipAddress in allowedIpAddresses: {
          value: ipAddress
        }
      ]
    }
    disableLocalAuth: true
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: publicNetworkAccess
  }
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
  name: 'pl-${searchName}'
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
          privateLinkServiceId: search.id
          groupIds: ['searchService']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${searchName}'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'default'
          properties: {
            privateDnsZoneId: privateDnsZoneId
          }
        }
      ]
    }
  }
}

output searchID string = search.id
output searchPrincipalId string = search.identity.principalId
output searchName string = search.name
