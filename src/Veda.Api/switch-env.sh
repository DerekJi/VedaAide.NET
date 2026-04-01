#!/bin/bash
# Switches between local and cloud user secrets by patching the UserSecretsId in Veda.Api.csproj.
#   local → UserSecretsId ends in ...a8
#   cloud → UserSecretsId ends in ...a9

LOCAL_ID="78511e53-5061-4af3-a532-980931a060a8"
CLOUD_ID="78511e53-5061-4af3-a532-980931a060a9"
CSPROJ="Veda.Api.csproj"

if [ "$1" == "local" ]; then
  sed -i "s/$CLOUD_ID/$LOCAL_ID/g" "$CSPROJ"
  echo "Switched to local (UserSecretsId: ...a8)"
elif [ "$1" == "cloud" ]; then
  sed -i "s/$LOCAL_ID/$CLOUD_ID/g" "$CSPROJ"
  echo "Switched to cloud (UserSecretsId: ...a9)"
else
  echo "Usage: ./switch-env.sh [local|cloud]"
fi
