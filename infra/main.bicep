targetScope = 'subscription'

// Common configurations
@description('Name of the environment')
param environmentName string
@description('Principal ID to grant access to the AI services. Leave empty to skip')
param myPrincipalId string
@description('Current principal type being used')
@allowed(['User', 'ServicePrincipal'])
param myPrincipalType string
@description('IP addresses to grant access to the AI services. Leave empty to skip')
param allowedIpAddresses string
var allowedIpAddressesArray = !empty(allowedIpAddresses) ? split(allowedIpAddresses, ',') : []
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
@description('Authentication type to use (recommended: identity)')
@allowed(['identity', 'accessKey'])
param authMode string
@description('Address prefixes for the spoke vNet')
param vnetAddressPrefixes array = ['10.0.0.0/16']
@description('Address prefix for the private endpoint subnet')
param privateEndpointSubnetAddressPrefix string = '10.0.0.0/24'
@description('Address prefix for the application subnet')
param appSubnetAddressPrefix string = '10.0.1.0/24'

// AI Services configurations
@description('Name of the AI Services account. Automatically generated if left blank')
param aiServicesName string = ''
@description('Name of the AI Hub resource. Automatically generated if left blank')
param aiHubName string = ''
@description('Name of the Storage Account. Automatically generated if left blank')
param storageName string = ''
@description('Name of the Key Vault. Automatically generated if left blank')
param keyVaultName string = ''
@description('Name of the Bot Service. Automatically generated if left blank')
param botName string = ''
@description('Whether to deploy Azure AI Search service')
param deploySearch bool
@description('Name of the AI Search Service. Automatically generated if left blank')
param searchName string = ''
@description('Whether to deploy shared private links from AI Search')
param deploySharedPrivateLinks bool = deploySearch
@description('Whether to deploy an AI Hub')
param deployAIHub bool = false
@description('Whether to deploy a sample AI Project')
param deployAIProject bool = false

// Other configurations
@description('Name of the Bot Service. Automatically generated if left blank')
param msiName string = ''
@description('Name of the Cosmos DB Account. Automatically generated if left blank')
param cosmosName string = ''
@description('Name of the App Service Plan. Automatically generated if left blank')
param appPlanName string = ''
@description('Name of the App Services Instance. Automatically generated if left blank')
param appName string = ''
@description('Whether to enable authentication (requires Entra App Developer role)')
param enableAuthentication bool

@description('Gen AI model name and version to deploy')
@allowed(['gpt-4,1106-Preview', 'gpt-4,0125-Preview', 'gpt-4o,2024-05-13', 'gpt-4o-mini,2024-07-18'])
param model string
@description('Tokens per minute capacity for the model. Units of 1000 (capacity = 10 means 10,000 tokens per minute)')
param modelCapacity int
@description('Language and version of the app to be deployed')
@allowed(['python|3.10', 'node|20-lts', 'dotnetcore|8.0'])
param stack string
@description('Chat implementation to be used')
@allowed(['chat-completions', 'assistant'])
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
  frontendApp: !empty(appName) ? appName : '${abbrs.webSitesAppService}${environmentName}-${uniqueSuffix}'
  backendApp: !empty(appName) ? appName : '${abbrs.webSitesAppService}be-${environmentName}-${uniqueSuffix}'
  bot: !empty(botName) ? botName : '${abbrs.cognitiveServicesBot}${environmentName}-${uniqueSuffix}'
  vnet: '${abbrs.networkVirtualNetworks}${environmentName}-${uniqueSuffix}'
  privateLinkSubnet: '${abbrs.networkVirtualNetworksSubnets}${environmentName}-pl-${uniqueSuffix}'
  appSubnet: '${abbrs.networkVirtualNetworksSubnets}${environmentName}-app-${uniqueSuffix}'
  aiServices: !empty(aiServicesName)
    ? aiServicesName
    : '${abbrs.cognitiveServicesAccounts}${environmentName}-${uniqueSuffix}'
  aiHub: !empty(aiHubName) ? aiHubName : '${abbrs.cognitiveServicesAccounts}hub-${environmentName}-${uniqueSuffix}'
  search: !empty(searchName) ? searchName : '${abbrs.searchSearchServices}${environmentName}-${uniqueSuffix}'
  storage: !empty(storageName)
    ? storageName
    : replace(replace('${abbrs.storageStorageAccounts}${environmentName}${uniqueSuffix}', '-', ''), '_', '')
  keyVault: !empty(keyVaultName) ? keyVaultName : '${abbrs.keyVaultVaults}${environmentName}-${uniqueSuffix}'
  computeInstance: '${abbrs.computeVirtualMachines}${environmentName}-${uniqueSuffix}'
}

// Private Network Resources
var dnsZones = [
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

var dnsZoneIds = publicNetworkAccess == 'Disabled' ? m_dns.outputs.dnsZoneIds : dnsZones
var privateEndpointSubnetId = publicNetworkAccess == 'Disabled' ? m_network.outputs.privateEndpointSubnetId : ''

// Deploy two resource groups
resource resourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: names.resourceGroup
  location: location
  tags: union(tags, { 'azd-env-name': environmentName })
}

resource dnsResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = if (publicNetworkAccess == 'Disabled') {
  name: names.dnsResourceGroup
  location: location
  tags: tags
}

// Network module - deploys Vnet
module m_network 'modules/aistudio/network.bicep' = if (publicNetworkAccess == 'Disabled') {
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
module m_dns 'modules/aistudio/dns.bicep' = if (publicNetworkAccess == 'Disabled') {
  name: 'deploy_dns'
  scope: dnsResourceGroup
  params: {
    vnetId: publicNetworkAccess == 'Disabled' ? m_network.outputs.vnetId : ''
    vnetName: publicNetworkAccess == 'Disabled' ? m_network.outputs.vnetName : ''
    dnsZones: dnsZones
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

// AI Services module
module m_aiservices 'modules/aistudio/aiservices.bicep' = {
  name: 'deploy_aiservices'
  scope: resourceGroup
  params: {
    location: location
    aiServicesName: names.aiServices
    publicNetworkAccess: publicNetworkAccess
    privateEndpointSubnetId: privateEndpointSubnetId
    openAIPrivateDnsZoneId: dnsZoneIds[0]
    cognitiveServicesPrivateDnsZoneId: dnsZoneIds[1]
    authMode: authMode
    grantAccessTo: authMode == 'identity'
      ? [
          {
            id: myPrincipalId
            type: myPrincipalType
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
      : []
    allowedIpAddresses: allowedIpAddressesArray
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
    privateEndpointSubnetId: privateEndpointSubnetId
    privateDnsZoneId: dnsZoneIds[4]
    allowedIpAddresses: allowedIpAddressesArray
    tags: tags
  }
}

module m_sharedPrivateLinks 'modules/aistudio/sharedPrivateLinks.bicep' = if (deploySharedPrivateLinks) {
  name: 'deploy_sharedPrivateLinks'
  scope: resourceGroup
  params: {
    searchName: names.search
    aiServicesName: m_aiservices.outputs.aiServicesName
    storageName: m_storage.outputs.storageName
    grantAccessTo: authMode == 'identity'
      ? [
          {
            id: myPrincipalId
            type: myPrincipalType
          }
          {
            id: m_msi.outputs.msiPrincipalID
            type: 'ServicePrincipal'
          }
          {
            id: m_aiservices.outputs.aiServicesPrincipalId
            type: 'ServicePrincipal'
          }
        ]
      : []
  }
}

// Storage and Key Vault
module m_storage 'modules/aistudio/storage.bicep' = {
  name: 'deploy_storage'
  scope: resourceGroup
  params: {
    location: location
    storageName: names.storage
    publicNetworkAccess: publicNetworkAccess
    authMode: authMode
    privateEndpointSubnetId: privateEndpointSubnetId
    privateDnsZoneId: dnsZoneIds[2]
    grantAccessTo: authMode == 'identity'
      ? [
          {
            id: myPrincipalId
            type: myPrincipalType
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
      : []
    tags: tags
  }
}

module m_keyVault 'modules/aistudio/keyVault.bicep' = {
  name: 'deploy_keyVault'
  scope: resourceGroup
  params: {
    location: location
    keyVaultName: names.keyVault
    publicNetworkAccess: publicNetworkAccess
    privateEndpointSubnetId: privateEndpointSubnetId
    privateDnsZoneId: dnsZoneIds[3]
    tags: tags
  }
}

// AI Hub module - deploys AI Hub and Project
module m_aihub 'modules/aistudio/aihub.bicep' = if (deployAIHub) {
  name: 'deploy_ai'
  scope: resourceGroup
  params: {
    location: location
    aiHubName: names.aiHub
    aiProjectName: 'cog-ai-prj-${environmentName}-${uniqueSuffix}'
    aiServicesName: m_aiservices.outputs.aiServicesName
    keyVaultName: m_keyVault.outputs.keyVaultName
    storageName: names.storage
    searchName: deploySearch ? m_search.outputs.searchName : ''
    publicNetworkAccess: publicNetworkAccess
    systemDatastoresAuthMode: authMode
    privateEndpointSubnetId: privateEndpointSubnetId
    apiPrivateDnsZoneId: dnsZoneIds[6]
    notebookPrivateDnsZoneId: dnsZoneIds[7]
    defaultComputeName: names.computeInstance
    deployAIProject: deployAIProject
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
    privateEndpointSubnetId: privateEndpointSubnetId
    privateDnsZoneId: dnsZoneIds[5]
    allowedIpAddresses: allowedIpAddressesArray
    authMode: startsWith(stack, 'dotnet') ? authMode : 'accessKey'
    grantAccessTo: authMode == 'identity'
      ? [
          {
            id: myPrincipalId
            type: myPrincipalType
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
      : []
    tags: tags
  }
}

module m_gpt 'modules/gptDeployment.bicep' = {
  name: 'deploygpt'
  scope: resourceGroup
  params: {
    aiServicesName: m_aiservices.outputs.aiServicesName
    modelName: modelName
    modelVersion: modelVersion
    modelCapacity: modelCapacity
  }
}

module m_app 'modules/appservice.bicep' = {
  name: 'deploy_app'
  scope: resourceGroup
  params: {
    location: location
    appServicePlanName: names.appPlan
    frontendAppServiceName: names.frontendApp
    backendAppServiceName: names.backendApp
    linuxFxVersion: stack
    implementation: implementation
    tags: tags
    publicNetworkAccess: publicNetworkAccess
    privateEndpointSubnetId: privateEndpointSubnetId
    privateDnsZoneId: dnsZoneIds[8]
    authMode: authMode
    appSubnetId: publicNetworkAccess == 'Disabled' ? m_network.outputs.appSubnetId : ''
    msiID: m_msi.outputs.msiID
    msiClientID: m_msi.outputs.msiClientID
    cosmosName: m_cosmos.outputs.cosmosName
    searchName: deploySearch ? m_search.outputs.searchName : ''
    deploymentName: m_gpt.outputs.modelName
    aiServicesName: m_aiservices.outputs.aiServicesName
  }
}

module m_bot 'modules/botservice.bicep' = {
  name: 'deploy_bot'
  scope: resourceGroup
  params: {
    location: 'global'
    botServiceName: names.bot
    tags: tags
    endpoint: 'https://${m_app.outputs.backendHostName}/api/messages'
    msiClientID: m_msi.outputs.msiClientID
    msiID: m_msi.outputs.msiID
    publicNetworkAccess: publicNetworkAccess
  }
}

output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP_ID string = resourceGroup.id
output AZURE_RESOURCE_GROUP_NAME string = resourceGroup.name
output AZURE_OPENAI_DEPLOYMENT_NAME string = m_gpt.outputs.modelName
output AI_SERVICES_ENDPOINT string = m_aiservices.outputs.aiServicesEndpoint
output FRONTEND_APP_NAME string = m_app.outputs.frontendAppName
output FRONTEND_APP_HOSTNAME string = m_app.outputs.frontendHostName
output BACKEND_APP_NAME string = m_app.outputs.backendAppName
output BACKEND_APP_HOSTNAME string = m_app.outputs.backendHostName
output BOT_NAME string = m_bot.outputs.name
output STACK string = split(stack, '|')[0]
output IMPLEMENTATION string = implementation
output ENABLE_AUTH bool = enableAuthentication
output AUTH_MODE string = authMode
