set -e

echo "Loading azd .env file from current environment..."

while IFS='=' read -r key value; do
    value=$(echo "$value" | sed 's/^"//' | sed 's/"$//')
    export "$key=$value"
done <<EOF
$(azd env get-values)
EOF

OAUTH_TOKEN=$(az account get-access-token --scope https://cognitiveservices.azure.com/.default --query accessToken -o tsv)
AOAI_ASSISTANT_NAME="assistant_in_a_box"
ASSISTANT_ID=$(curl "$AI_SERVICES_ENDPOINT/openai/assistants?api-version=2024-07-01-preview" \
  -H "Authorization: Bearer $OAUTH_TOKEN" | \
  jq -r '[.data[] | select( .name == "'$AOAI_ASSISTANT_NAME'")][0] | .id') 
if [ "$ASSISTANT_ID" = "null" ]; then
    echo "Assistant is null"
    ASSISTANT_ID=
else
    echo "Assistant is not null"
    echo $ASSISTANT_ID
    ASSISTANT_ID=/$ASSISTANT_ID
    echo $ASSISTANT_ID
fi

echo '{
    "name":"'$AOAI_ASSISTANT_NAME'",
    "model":"'$AZURE_OPENAI_DEPLOYMENT_NAME'",
    "instructions":"",
    "tools": '$(cat ./assistants_tools/*.json | jq -s)',
    "metadata":{}
  }' > tmp.json
curl "$AI_SERVICES_ENDPOINT/openai/assistants$ASSISTANT_ID?api-version=2024-07-01-preview" \
  -H "Authorization: Bearer $OAUTH_TOKEN" \
  -H 'content-type: application/json' \
  -d @tmp.json \
  --fail-with-body
rm tmp.json

echo "Assistant created"

ASSISTANT_ID=$(curl "$AI_SERVICES_ENDPOINT/openai/assistants?api-version=2024-07-01-preview" \
  -H "Authorization: Bearer $OAUTH_TOKEN"|\
  jq -r '[.data[] | select( .name == "'$AOAI_ASSISTANT_NAME'")][0] | .id')

echo "Assistant fetched"

az webapp config appsettings set -g $AZURE_RESOURCE_GROUP_NAME -n $BACKEND_APP_NAME --settings AZURE_OPENAI_ASSISTANT_ID=$ASSISTANT_ID

echo "App configured"

azd env set AZURE_ASSISTANT_ID $ASSISTANT_ID