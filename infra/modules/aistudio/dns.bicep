param vnetName string
param vnetId string
param tags object = {}

param dnsZones array

resource privateDnsZones 'Microsoft.Network/privateDnsZones@2020-06-01' = [
  for zone in dnsZones: {
    name: zone
    location: 'global'
    tags: tags
    properties: {}
  }
]

resource virtualNetworkLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [
  for zone in dnsZones: {
    name: '${zone}/${vnetName}-link'
    location: 'global'
    tags: tags
    properties: {
      registrationEnabled: false
      virtualNetwork: {
        id: vnetId
      }
    }
    dependsOn: [
      privateDnsZones
    ]
  }
]

output dnsZoneNames string[] = [for zone in dnsZones: zone]
output dnsZoneIds string[] = [for (zone, index) in dnsZones: privateDnsZones[index].id]
