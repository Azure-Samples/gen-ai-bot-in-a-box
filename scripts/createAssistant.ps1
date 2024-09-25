Write-Host "Loading azd .env file from current environment..."

$envValues = azd env get-values
$envValues.Split("`n") | ForEach-Object {
    $key, $value = $_.Split('=')
    $value = $value.Trim('"')
    Set-Variable -Name $key -Value $value -Scope Global
}

$OAUTH_TOKEN=$(az account get-access-token --scope https://cognitiveservices.azure.com/.default --query accessToken -o tsv)
$AOAI_ASSISTANT_NAME="assistant_in_a_box"
$ASSISTANT_ID=((curl "${AI_SERVICES_ENDPOINT}openai/assistants`?api-version=2024-07-01-preview" -H "Authorization: Bearer $OAUTH_TOKEN" | ConvertFrom-Json).data | Where-Object name -eq $AOAI_ASSISTANT_NAME).id

if ( $ASSISTANT_ID -eq $null )    
    {
        $ASSISTANT_ID=""
    }
else
    {
        $ASSISTANT_ID="/$ASSISTANT_ID"
    }

$TOOLS=""
Get-ChildItem "assistants_tools" -Filter *.json | 
          Foreach-Object {
              $content = Get-Content $_.FullName
              if ($TOOLS -eq "") {
                  $TOOLS = $content
              } else {
                  $TOOLS = "$TOOLS,$content"
              }
          }

echo "{
    `"name`":`"${AOAI_ASSISTANT_NAME}`",
    `"model`":`"${AOAI_DEPLOYMENT_NAME}`",
    `"instructions`":`"`",
    `"tools`":[
        $TOOLS
    ],
    `"metadata`":{}
  }" | Out-File tmp.json
curl "${AI_SERVICES_ENDPOINT}openai/assistants$ASSISTANT_ID`?api-version=2024-07-01-preview" -H "Authorization: Bearer $OAUTH_TOKEN" -H 'content-type: application/json' -d '@tmp.json'

rm tmp.json

$ASSISTANT_ID=((curl "${AI_SERVICES_ENDPOINT}openai/assistants`?api-version=2024-07-01-preview" -H "Authorization: Bearer $OAUTH_TOKEN" | ConvertFrom-Json).data | Where-Object name -eq $AOAI_ASSISTANT_NAME).id

az webapp config appsettings set -g $AZURE_RESOURCE_GROUP_NAME -n $BACKEND_APP_NAME --settings AZURE_OPENAI_ASSISTANT_ID=$ASSISTANT_ID

azd env set AZURE_ASSISTANT_ID $ASSISTANT_ID
