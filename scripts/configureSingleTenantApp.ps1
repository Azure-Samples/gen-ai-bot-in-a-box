# Enable error handling
$ErrorActionPreference = "Stop"


# Set environment variables from azd deployment
$envValues = azd env get-values | ConvertFrom-StringData
foreach ($key in $envValues.Keys) {
    $value = $envValues[$key].Trim('"')
    [System.Environment]::SetEnvironmentVariable($key, $value)
}

if ($envValues["STACK"] -ne "python") {
    return
}

Write-Output "Switching MSI to Single Tenant mode..."

# Create an App Registration and retrieve its ID and Client ID.
$app = az ad app create --display-name $env:BACKEND_APP_NAME --web-redirect-uris "https://token.botframework.com/.auth/web/redirect" | ConvertFrom-Json
$appId = $app.id
$clientId = $app.appId

# Create a client secret for the newly created app
$secret = az ad app credential reset --id $appId | ConvertFrom-Json
$clientSecret = $secret.password

# Delete the existing bot
az bot delete --name $env:BOT_NAME --resource-group $env:AZURE_RESOURCE_GROUP_NAME --yes

# Recreate the bot with Single Tenant registration
az bot create --name $env:BOT_NAME --resource-group $env:AZURE_RESOURCE_GROUP_NAME --app-type SingleTenant --appid $clientId --tenant-id $env:AZURE_TENANT_ID -e "https://$($env:BACKEND_APP_NAME).azurewebsites.net/api/messages" -l global

# Configure the App Service with Single Tenant app credentials.
az webapp config appsettings set -g $env:AZURE_RESOURCE_GROUP_NAME -n $env:BACKEND_APP_NAME --settings MicrosoftAppId=$clientId MicrosoftAppPassword=$clientSecret MicrosoftAppType=SingleTenant