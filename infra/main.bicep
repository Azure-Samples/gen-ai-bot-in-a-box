targetScope = 'subscription'

// Common configurations
@description('Name of the environment')
param environmentName string
@description('Principal ID to grant access to the AI services. Leave empty to skip')
param myPrincipalId string = ''
@description('Resource group name for the AI services. Defauts to rg-<environmentName>')
param resourceGroupName string = ''
@description('Resource group name for the DNS configurations. Defaults to rg-dns')
param dnsResourceGroupName string = ''
@description('Tags for all AI resources created. JSON object')
param tags object = {}

// Network configurations
@description('Allow or deny public network access to the AI services (recommended: Disabled)')
@allowed(['Enabled', 'Disabled'])
param publicNetworkAccess string
@description('Authentication type to use with Storage Account (recommended: identity)')
@allowed(['identity', 'accessKey'])
param systemDatastoresAuthMode string
@description('Address prefixes for the spoke vNet')
param vnetAddressPrefixes array = ['10.0.0.0/16']
@description('Address prefix for the private endpoint subnet')
param privateEndpointSubnetAddressPrefix string = '10.0.0.0/24'
@description('Address prefix for the application subnet')
param appSubnetAddressPrefix string = '10.0.1.0/24'

// AI Services configurations
@description('Name of the AI Services account. Automatically generated if left blank')
param aiServicesName string = ''
@description('Name of the Storage Account. Automatically generated if left blank')
param storageName string = ''
@description('Name of the Bot Service. Automatically generated if left blank')
param botName string = ''
@description('Whether to deploy Azure AI Search service')
param deploySearch bool
@description('Name of the AI Search Service. Automatically generated if left blank')
param searchName string = ''
@description('Whether to deploy shared private links from AI Search')
param deploySharedPrivateLinks bool = deploySearch


// Other configurations
@description('Name of the Bot Service. Automatically generated if left blank')
param msiName string = ''
@description('Name of the Cosmos DB Account. Automatically generated if left blank')
param cosmosName string = ''
@description('Name of the App Service Plan. Automatically generated if left blank')
param appPlanName string = ''
@description('Name of the App Services Instance. Automatically generated if left blank')
param appName string = ''

@description('Gen AI model name and version to deploy')
@allowed(['gpt-4,1106-Preview', 'gpt-4,0125-Preview'])
param model string
@description('Language and version of the app to be deployed')
@allowed(['python|3.10', 'node|14', 'dotnetcore|8.0'])
param stack string
@description('Chat implementation to be used')
@allowed(['chat-completions', 'assistants'])
param implementation string

var modelName = split(model, ',')[0]
var modelVersion = split(model, ',')[1]

var abbrs = loadJsonContent('abbreviations.json')
var uniqueSuffix = substring(uniqueString(subscription().id, environmentName), 1, 3)
var location = deployment().location

var names = {
  resourceGroup: !empty(resourceGroupName) ? resourceGroupName : '${abbrs.resourcesResourceGroups}${environmentName}'
  dnsResourceGroup: !empty(dnsResourceGroupName) ? dnsResourceGroupName : '${abbrs.resourcesResourceGroups}dns'
  msi: !empty(msiName) ? msiName : '${abbrs.managedIdentityUserAssignedIdentities}${environmentName}-${uniqueSuffix}'
  cosmos: !empty(cosmosName) ? cosmosName : '${abbrs.documentDBDatabaseAccounts}${environmentName}-${uniqueSuffix}'
  appPlan: !empty(appPlanName)
    ? appPlanName
    : '${abbrs.webSitesAppServiceEnvironment}${environmentName}-${uniqueSuffix}'
  app: !empty(appName) ? appName : '${abbrs.webSitesAppService}${environmentName}-${uniqueSuffix}'
  bot: !empty(botName) ? botName : '${abbrs.cognitiveServicesBot}${environmentName}-${uniqueSuffix}'
  vnet: '${abbrs.networkVirtualNetworks}${environmentName}-${uniqueSuffix}'
  privateLinkSubnet: '${abbrs.networkVirtualNetworksSubnets}${environmentName}-pl-${uniqueSuffix}'
  appSubnet: '${abbrs.networkVirtualNetworksSubnets}${environmentName}-app-${uniqueSuffix}'
  aiServices: !empty(aiServicesName) ? aiServicesName : '${abbrs.cognitiveServicesAccounts}${environmentName}-${uniqueSuffix}'
  search: !empty(searchName) ? searchName : '${abbrs.searchSearchServices}${environmentName}-${uniqueSuffix}'
  storage: !empty(storageName)
    ? storageName
    : replace(replace('${abbrs.storageStorageAccounts}${environmentName}${uniqueSuffix}', '-', ''), '_', '')
  computeInstance: '${abbrs.computeVirtualMachines}${environmentName}-${uniqueSuffix}'
}

// Deploy two resource groups
resource resourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: names.resourceGroup
  location: location
  tags: tags
}

resource dnsResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: names.dnsResourceGroup
  location: location
  tags: tags
}


// Network module - deploys Vnet
module m_network 'modules/aistudio/network.bicep' = {
  name: 'deploy_vnet'
  scope: resourceGroup
  params: {
    location: location
    vnetName: names.vnet
    vnetAddressPrefixes: vnetAddressPrefixes
    privateEndpointSubnetName: names.privateLinkSubnet
    privateEndpointSubnetAddressPrefix: privateEndpointSubnetAddressPrefix
    appSubnetName: names.appSubnet
    appSubnetAddressPrefix: appSubnetAddressPrefix
  }
}

// DNS module - deploys private DNS zones and links them to the Vnet
module m_dns 'modules/aistudio/dns.bicep' = {
  name: 'deploy_dns'
  scope: dnsResourceGroup
  params: {
    vnetId: m_network.outputs.vnetId
    vnetName: m_network.outputs.vnetName
    dnsZones: [
      'privatelink.openai.azure.com'
      'privatelink.cognitiveservices.azure.com'
      'privatelink.blob.${environment().suffixes.storage}'
      'privatelink.vault.azure.com'
      'privatelink.search.azure.com'
      'privatelink.documents.azure.com'
      'privatelink.api.azureml.ms'
      'privatelink.notebooks.azure.net'
      'privatelink.azurewebsites.net'
    ]
  }
}

module m_msi 'modules/msi.bicep' = {
  name: 'deploy_msi'
  scope: resourceGroup
  params: {
    location: location
    msiName: names.msi
    tags: tags
  }
}

// AI Services modules - deploy Cognitive Services and AI Search
module m_aiServices 'modules/aistudio/aiServices.bicep' = {
  name: 'deploy_aiServices'
  scope: resourceGroup
  params: {
    location: location
    aiServicesName: names.aiServices
    publicNetworkAccess: publicNetworkAccess
    privateEndpointSubnetId: m_network.outputs.privateEndpointSubnetId
    openAIPrivateDnsZoneId: m_dns.outputs.dnsZoneIds[0]
    cognitiveServicesPrivateDnsZoneId: m_dns.outputs.dnsZoneIds[1]
    grantAccessTo: [
      {
        id: myPrincipalId
        type: 'User'
      }
      {
        id: m_msi.outputs.msiPrincipalID
        type: 'ServicePrincipal'
      }
      {
        id: deploySearch ? m_search.outputs.searchPrincipalId : ''
        type: 'ServicePrincipal'
      }
    ]
    tags: tags
  }
}

module m_search 'modules/aistudio/searchService.bicep' = if (deploySearch) {
  name: 'deploy_search'
  scope: resourceGroup
  params: {
    location: location
    searchName: names.search
    publicNetworkAccess: publicNetworkAccess
    privateEndpointSubnetId: m_network.outputs.privateEndpointSubnetId
    privateDnsZoneId: m_dns.outputs.dnsZoneIds[4]
    tags: tags
  }
}

module m_sharedPrivateLinks 'modules/aistudio/sharedPrivateLinks.bicep' = if (deploySharedPrivateLinks) {
  name: 'deploy_sharedPrivateLinks'
  scope: resourceGroup
  params: {
    searchName: names.search
    aiServicesName: m_aiServices.outputs.aiServicesName
    storageName: m_storage.outputs.storageName
    grantAccessTo: [
      {
        id: myPrincipalId
        type: 'User'
      }
      {
        id: m_msi.outputs.msiPrincipalID
        type: 'ServicePrincipal'
      }
      {
        id: m_aiServices.outputs.aiServicesPrincipalId
        type: 'ServicePrincipal'
      }
    ]
  }
}

// Storage and Key Vault - AI Hub dependencies
module m_storage 'modules/aistudio/storage.bicep' = {
  name: 'deploy_storage'
  scope: resourceGroup
  params: {
    location: location
    storageName: names.storage
    publicNetworkAccess: publicNetworkAccess
    systemDatastoresAuthMode: systemDatastoresAuthMode
    privateEndpointSubnetId: m_network.outputs.privateEndpointSubnetId
    privateDnsZoneId: m_dns.outputs.dnsZoneIds[2]
    grantAccessTo: [
      {
        id: myPrincipalId
        type: 'User'
      }
      {
        id: m_msi.outputs.msiPrincipalID
        type: 'ServicePrincipal'
      }
      {
        id: deploySearch ? m_search.outputs.searchPrincipalId : ''
        type: 'ServicePrincipal'
      }
    ]
    tags: tags
  }
}

module m_cosmos 'modules/cosmos.bicep' = {
  name: 'deploy_cosmos'
  scope: resourceGroup
  params: {
    location: location
    cosmosName: names.cosmos
    publicNetworkAccess: publicNetworkAccess
    privateEndpointSubnetId: m_network.outputs.privateEndpointSubnetId
    privateDnsZoneId: m_dns.outputs.dnsZoneIds[5]
    grantAccessTo: [
      {
        id: myPrincipalId
        type: 'User'
      }
      {
        id: m_msi.outputs.msiPrincipalID
        type: 'ServicePrincipal'
      }
    ]
    tags: tags
  }
}

module m_gpt 'modules/gptDeployment.bicep' = {
  name: 'deploygpt'
  scope: resourceGroup
  params: {
    aiServicesName: m_aiServices.outputs.aiServicesName
    modelName: modelName
    modelVersion: modelVersion
  }
}

module m_app 'modules/appservice.bicep' = {
  name: 'deploy_app'
  scope: resourceGroup
  params: {
    location: location
    appServicePlanName: names.appPlan
    appServiceName: names.app
    linuxFxVersion: stack
    implementation: implementation
    tags: tags
    privateEndpointSubnetId: m_network.outputs.privateEndpointSubnetId
    privateDnsZoneId: m_dns.outputs.dnsZoneIds[8]
    appSubnetId: m_network.outputs.appSubnetId
    msiID: m_msi.outputs.msiID
    msiClientID: m_msi.outputs.msiClientID
    cosmosName: m_cosmos.outputs.cosmosName
    searchName: deploySearch ? m_search.outputs.searchName : ''
    deploymentName: m_gpt.outputs.modelName
    aiServicesName: m_aiServices.outputs.aiServicesName
  }
}

module m_bot 'modules/botservice.bicep' = {
  name: 'deploy_bot'
  scope: resourceGroup
  params: {
    location: 'global'
    botServiceName: names.bot
    tags: tags
    endpoint: 'https://${m_app.outputs.hostName}/api/messages'
    msiClientID: m_msi.outputs.msiClientID
    msiID: m_msi.outputs.msiID
    publicNetworkAccess: publicNetworkAccess
  }
}

output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP_ID string = resourceGroup.id
output AZURE_RESOURCE_GROUP_NAME string = resourceGroup.name
output APP_NAME string = m_app.outputs.appName
output APP_HOSTNAME string = m_app.outputs.hostName
output BOT_NAME string = m_bot.outputs.name
output STACK string = split(stack, '|')[0]
