set -e


# Set environment variables from azd deployment
while IFS='=' read -r key value; do
    value=$(echo "$value" | sed 's/^"//' | sed 's/"$//')
    export "$key=$value"
done <<EOF
$(azd env get-values)
EOF

if [ "$STACK" != "python" ]; then
    exit 0
fi

echo "Deploying Python app - Switching MSI to Single Tenant mode..."

# Create an App Registration and retrieve its ID and Client ID.
APP=$(az ad app create --display-name $APP_NAME --web-redirect-uris https://token.botframework.com/.auth/web/redirect)
APP_ID=$(echo $APP | jq -r .id)
CLIENT_ID=$(echo $APP | jq -r .appId)

# Create a client secret for the newly created app
SECRET=$(az ad app credential reset --id $APP_ID)
CLIENT_SECRET=$(echo $SECRET | jq -r .password)

# Delete the existing bot
az bot delete --name $BOT_NAME --resource-group $AZURE_RESOURCE_GROUP_NAME

# Recreate the bot with Single Tenant registration
az bot create --name $BOT_NAME --resource-group $AZURE_RESOURCE_GROUP_NAME --app-type SingleTenant --appid $CLIENT_ID --tenant-id $AZURE_TENANT_ID -e https://$APP_NAME.azurewebsites.net/api/messages -l global

# Configure the App Service with Single Tenant app credentials.
az webapp config appsettings set -g $AZURE_RESOURCE_GROUP_NAME -n $APP_NAME --settings MicrosoftAppId=$CLIENT_ID MicrosoftAppPassword=$CLIENT_SECRET MicrosoftAppType=SingleTenant