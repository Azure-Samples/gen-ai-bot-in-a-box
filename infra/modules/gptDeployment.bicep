param aiServicesName string
param modelName string
param modelVersion string
param modelCapacity int = 10

resource aiServices 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: aiServicesName

  resource gptdeployment 'deployments' = if (startsWith(modelName, 'gpt')) {
    name: modelName
    properties: {
      model: {
        format: 'OpenAI'
        name: modelName
        version: modelVersion
      }
    }
    sku: {
      capacity: modelCapacity
      name: 'Standard'
    }
  }
}

output modelName string = aiServices::gptdeployment.name
output modelVersion string = aiServices::gptdeployment.properties.model.version
