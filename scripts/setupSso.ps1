Write-Host "Loading azd .env file from current environment..."

$envValues = azd env get-values
$envValues.Split("`n") | ForEach-Object {
    $key, $value = $_.Split('=')
    $value = $value.Trim('"')
    Set-Variable -Name $key -Value $value -Scope Global
}

if ($envValues["ENABLE_AUTH"] -ne "true") {
    return
}

# If App Registration was not created, create it
if ( $CLIENT_ID -eq $null )    
    {
        $APP = az ad app create --display-name $BACKEND_APP_NAME --web-redirect-uris https://$FRONTEND_APP_NAME.azurewebsites.net/.auth/login/aad/callback --enable-id-token-issuance | ConvertFrom-Json
        $APP_ID = $APP.id
        $CLIENT_ID = $APP.appId
    }

# Set up authorization for frontend app
az webapp auth config-version upgrade -g $AZURE_RESOURCE_GROUP_NAME -n $FRONTEND_APP_NAME || true
az webapp auth update -g $AZURE_RESOURCE_GROUP_NAME -n $FRONTEND_APP_NAME --enabled true \
    --action RedirectToLoginPage  --redirect-provider azureactivedirectory
az webapp auth microsoft update -g $AZURE_RESOURCE_GROUP_NAME -n $FRONTEND_APP_NAME \
    --allowed-token-audiences https://$FRONTEND_APP_NAME.azurewebsites.net/.auth/login/aad/callback \
    --client-id $CLIENT_ID \
    --issuer https://sts.windows.net/$AZURE_TENANT_ID/
