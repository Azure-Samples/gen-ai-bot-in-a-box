param location string
param appServicePlanName string
param backendAppServiceName string
param frontendAppServiceName string
param msiID string
param msiClientID string
param linuxFxVersion string
param implementation string
param sku string = 'P0v3'
param tags object = {}
param deploymentName string
param searchName string

param aiServicesName string
param cosmosName string

param authMode string
param publicNetworkAccess string
param privateEndpointSubnetId string
param appSubnetId string
param privateDnsZoneId string

var backendLanguage = startsWith(linuxFxVersion, 'python') ? 'python' : startsWith(linuxFxVersion, 'node') ? 'node' : 'dotnet'

resource aiServices 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: aiServicesName
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosName
}

resource search 'Microsoft.Search/searchServices@2023-11-01' existing = if (!empty(searchName)) {
  name: searchName
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: sku
    capacity: 1
  }
  properties: {
    reserved: true
  }
  kind: 'linux'
}

resource backend 'Microsoft.Web/sites@2023-12-01' = {
  name: backendAppServiceName
  location: location
  tags: union(tags, { 'azd-service-name': 'genai-bot-app-backend' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${msiID}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    virtualNetworkSubnetId: !empty(appSubnetId) ? appSubnetId : null
    siteConfig: {
      ipSecurityRestrictions: [
        // Allow Bot Service
        { action: 'Allow', ipAddress: 'AzureBotService', priority: 100, tag: 'ServiceTag' }
        // Allow Teams Messaging IPs
        { action: 'Allow', ipAddress: '13.107.64.0/18', priority: 200 }
        { action: 'Allow', ipAddress: '52.112.0.0/14', priority: 201 }
        { action: 'Allow', ipAddress: '52.120.0.0/14', priority: 202 }
        { action: 'Allow', ipAddress: '52.238.119.141/32', priority: 203 }
      ]
      publicNetworkAccess: 'Enabled'
      ipSecurityRestrictionsDefaultAction: 'Deny'
      scmIpSecurityRestrictionsDefaultAction: 'Allow'
      http20Enabled: true
      linuxFxVersion: linuxFxVersion
      webSocketsEnabled: true
      appCommandLine: startsWith(linuxFxVersion, 'python')
        ? 'gunicorn --bind 0.0.0.0 --timeout 600 app:app --worker-class aiohttp.GunicornWebWorker'
        : ''
      appSettings: [
        {
          name: 'MicrosoftAppType'
          value: 'UserAssignedMSI'
        }
        {
          name: 'MicrosoftAppId'
          value: msiClientID
        }
        {
          name: 'MicrosoftAppTenantId'
          value: tenant().tenantId
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: msiClientID
        }
        {
          name: 'AZURE_TENANT_ID'
          value: tenant().tenantId
        }
        {
          name: 'SSO_ENABLED'
          value: 'false'
        }
        {
          name: 'SSO_CONFIG_NAME'
          value: ''
        }
        {
          name: 'SSO_MESSAGE_TITLE'
          value: 'Please sign in to continue.'
        }
        {
          name: 'SSO_MESSAGE_PROMPT'
          value: 'Sign in'
        }
        {
          name: 'SSO_MESSAGE_SUCCESS'
          value: 'User logged in successfully! Please repeat your question.'
        }
        {
          name: 'SSO_MESSAGE_FAILED'
          value: 'Log in failed. Type anything to retry.'
        }
        {
          name: 'GEN_AI_IMPLEMENTATION'
          value: implementation
        }
        {
          name: 'AZURE_OPENAI_API_ENDPOINT'
          value: aiServices.properties.endpoint
        }
        {
          name: 'AZURE_OPENAI_API_VERSION'
          value: '2024-05-01-preview'
        }
        {
          name: 'AZURE_OPENAI_DEPLOYMENT_NAME'
          value: deploymentName
        }
        {
          name: 'AZURE_OPENAI_ASSISTANT_ID'
          value: 'YOUR_ASSISTANT_ID'
        }
        {
          name: 'AZURE_OPENAI_STREAMING'
          value: 'true'
        }
        {
          name: 'AZURE_OPENAI_API_KEY'
          value: authMode == 'accessKey' ? aiServices.listKeys().key1 : ''
        }
        {
          name: 'AZURE_COSMOSDB_ENDPOINT'
          value: cosmos.properties.documentEndpoint
        }
        {
          name: 'AZURE_COSMOSDB_DATABASE_ID'
          value: 'GenAIBot'
        }
        {
          name: 'AZURE_COSMOSDB_CONTAINER_ID'
          value: 'Conversations'
        }
        {
          name: 'AZURE_COSMOSDB_AUTH_KEY'
          value: backendLanguage != 'dotnet' || authMode == 'accessKey' ? cosmos.listKeys().primaryMasterKey : ''
        }
        {
          name: 'AZURE_SEARCH_API_ENDPOINT'
          value: !empty(searchName) ? 'https://${search.name}.search.windows.net' : ''
        }
        {
          name: 'AZURE_SEARCH_INDEX'
          value: 'index-name'
        }
        {
          name: 'AZURE_SEARCH_API_KEY'
          value: !empty(searchName) && authMode == 'accessKey' ? search.listAdminKeys().primaryKey : ''
        }
        {
          name: 'MAX_TURNS'
          value: '10'
        }
        {
          name: 'LLM_WELCOME_MESSAGE'
          value: 'Hello and welcome!'
        }
        {
          name: 'LLM_INSTRUCTIONS'
          value: 'Answer the questions as accurately as possible using the provided functions.'
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'true'
        }
        {
          name: 'ENABLE_ORYX_BUILD'
          value: startsWith(linuxFxVersion, 'dotnet') ? 'false' : 'true'
        }
        {
          name: 'DEBUG'
          value: 'true'
        }
      ]
    }
  }
}

resource frontend 'Microsoft.Web/sites@2023-12-01' = {
  name: frontendAppServiceName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${msiID}': {}
    }
  }
  tags: union(tags, { 'azd-service-name': 'genai-bot-app-frontend' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    virtualNetworkSubnetId: !empty(appSubnetId) ? appSubnetId : null
    siteConfig: {
      publicNetworkAccess: 'Enabled'
      ipSecurityRestrictionsDefaultAction: 'Allow'
      scmIpSecurityRestrictionsDefaultAction: 'Allow'
      http20Enabled: true
      linuxFxVersion: 'NODE|20-lts'
      appSettings: [
        {
          name: 'MicrosoftAppType'
          value: 'UserAssignedMSI'
        }
        {
          name: 'MicrosoftAppId'
          value: msiClientID
        }
        {
          name: 'MicrosoftAppTenantId'
          value: tenant().tenantId
        }
        {
          name: 'AZURE_OPENAI_API_ENDPOINT'
          value: aiServices.properties.endpoints['OpenAI Realtime API']
        }
        {
          name: 'AZURE_OPENAI_API_VERSION'
          value: '2024-05-01-preview'
        }
        {
          name: 'AZURE_SPEECH_API_ENDPOINT'
          value: aiServices.properties.endpoint
        }
        {
          name: 'AZURE_SPEECH_REGION'
          value: aiServices.location
        }
        {
          name: 'AZURE_SPEECH_RESOURCE_ID'
          value: aiServices.id
        }
        {
          name: 'DIRECT_LINE_SECRET'
          value: 'YOUR_DIRECT_LINE_SECRET'
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'true'
        }
        {
          name: 'ENABLE_ORYX_BUILD'
          value: 'true'
        }
        {
          name: 'DEBUG'
          value: 'true'
        }
      ]
    }
  }
}

resource backendAppPrivateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
  name: 'pl-${backendAppServiceName}'
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
          privateLinkServiceId: backend.id
          groupIds: ['sites']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${backendAppServiceName}'
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

resource frontentdAppPrivateEndpoint 'Microsoft.Network/privateEndpoints@2021-05-01' = if (publicNetworkAccess == 'Disabled') {
  name: 'pl-${frontendAppServiceName}'
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
          privateLinkServiceId: frontend.id
          groupIds: ['sites']
        }
      }
    ]
  }
  resource privateDnsZoneGroup 'privateDnsZoneGroups' = {
    name: 'zg-${frontendAppServiceName}'
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

output frontendAppName string = frontend.name
output frontendHostName string = frontend.properties.defaultHostName

output backendAppName string = backend.name
output backendHostName string = backend.properties.defaultHostName
