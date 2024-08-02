param location string
param vnetName string
param vnetAddressPrefixes array
param privateEndpointSubnetName string
param privateEndpointSubnetAddressPrefix string
param appSubnetName string
param appSubnetAddressPrefix string
param tags object = {}

resource vnet 'Microsoft.Network/virtualNetworks@2020-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: vnetAddressPrefixes
    }
    subnets: [
      {
        name: privateEndpointSubnetName
        properties: {
          addressPrefix: privateEndpointSubnetAddressPrefix
        }
      }
      {
        name: appSubnetName
        properties: {
          addressPrefix: appSubnetAddressPrefix
          delegations: [
            {
              name: 'default'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output privateEndpointSubnetId string = vnet.properties.subnets[0].id
output appSubnetId string = vnet.properties.subnets[1].id
