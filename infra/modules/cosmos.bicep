param location string
param cosmosName string
param tags object = {}
param publicNetworkAccess string
param privateEndpointSubnetId string
param privateDnsZoneId string
param grantAccessTo array = []

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
  name: cosmosName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    databaseAccountOfferType: 'Standard'
    publicNetworkAccess: publicNetworkAccess
    disableLocalAuth: true
  }

  resource db 'sqlDatabases' = {
    name: 'GenAIBot'
    properties: {
      resource: {
        id: 'GenAIBot'
      }
    }

    resource col 'containers' = {
      name: 'Conversations'
      properties: {
        resource: {
          id: 'Conversations'
          partitionKey: {
            paths: ['/id']
            kind: 'Hash'
          }
        }
      }
    }
  }
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
  name: 'pl-${cosmosName}'
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
          privateLinkServiceId: cosmos.id
          groupIds: ['Sql']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${cosmosName}'
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

resource cosmosDataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions@2021-10-15' existing = {
  name: '00000000-0000-0000-0000-000000000002'
  parent: cosmos
}

resource writerAccess 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-04-15' = [
  for principal in grantAccessTo: if (!empty(principal.id)) {
    name: guid(principal.id, cosmos.id, cosmosDataContributor.id)
    parent: cosmos
    properties: {
      roleDefinitionId: cosmosDataContributor.id
      principalId: principal.id
      scope: cosmos.id
    }
  }
]

output cosmosID string = cosmos.id
output cosmosName string = cosmos.name
