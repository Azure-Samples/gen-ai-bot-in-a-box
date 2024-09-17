set -e

echo "Loading azd .env file from current environment..."

while IFS='=' read -r key value; do
    value=$(echo "$value" | sed 's/^"//' | sed 's/"$//')
    export "$key=$value"
done <<EOF
$(azd env get-values)
EOF

if [ "$ENABLE_AUTH" != "true" ]; then
    echo "Skipping SSO configuration..."
    exit 0
fi

echo "Setting up SSO"

# If App Registration was not created, create it
if [ -z "$CLIENT_ID" ]; then
    echo "Creating app registration..."
    APP=$(az ad app create \
        --display-name $BACKEND_APP_NAME \
        --web-redirect-uris https://$FRONTEND_APP_NAME.azurewebsites.net/.auth/login/aad/callback \
        --enable-id-token-issuance \
        --required-resource-accesses '[{
            "resourceAppId": "00000003-0000-0000-c000-000000000000",
            "resourceAccess": [
                {
                    "id": "37f7f235-527c-4136-accd-4a02d197296e",
                    "type": "Scope"
                }
           ]
        }]' \
    )
    APP_ID=$(echo $APP | jq -r .id)
    CLIENT_ID=$(echo $APP | jq -r .appId)
fi

# Set up authorization for frontend app
az webapp auth config-version upgrade -g $AZURE_RESOURCE_GROUP_NAME -n $FRONTEND_APP_NAME || true
az webapp auth update -g $AZURE_RESOURCE_GROUP_NAME -n $FRONTEND_APP_NAME --enabled true \
    --action RedirectToLoginPage  --redirect-provider azureactivedirectory
az webapp auth microsoft update -g $AZURE_RESOURCE_GROUP_NAME -n $FRONTEND_APP_NAME \
    --allowed-token-audiences https://$FRONTEND_APP_NAME.azurewebsites.net/.auth/login/aad/callback \
    --client-id $CLIENT_ID \
    --issuer https://sts.windows.net/$AZURE_TENANT_ID/