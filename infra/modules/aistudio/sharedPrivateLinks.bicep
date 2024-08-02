param searchName string

param storageName string
param aiServicesName string
param grantAccessTo array

resource aiServices 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: aiServicesName
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' existing = {
  name: searchName

  resource linkToStorage 'sharedPrivateLinkResources' = {
    name: 'link-to-storage-account'
    properties: {
      groupId: 'blob'
      privateLinkResourceId: storage.id
      requestMessage: 'Requested Private Endpoint Connection from Search Service ${searchName}'
    }
  }
  resource linkToAI 'sharedPrivateLinkResources' = {
    name: 'link-to-ai-service'
    properties: {
      groupId: 'openai_account'
      privateLinkResourceId: aiServices.id
      requestMessage: 'Requested Private Endpoint Connection from Search Service ${searchName}'
    }
    dependsOn: [
      linkToStorage
    ]
  }
}

resource searchServiceContributor 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
}

resource searchIndexDataContributor 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
}

resource serviceAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in grantAccessTo: if (!empty(principal.id)) {
    name: guid(principal.id, search.id, searchServiceContributor.id)
    scope: search
    properties: {
      roleDefinitionId: searchServiceContributor.id
      principalId: principal.id
      principalType: principal.type
    }
  }
]

resource indexAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in grantAccessTo: if (!empty(principal.id)) {
    name: guid(principal.id, search.id, searchIndexDataContributor.id)
    scope: search
    properties: {
      roleDefinitionId: searchIndexDataContributor.id
      principalId: principal.id
      principalType: principal.type
    }
  }
]
