# VedaAide Infrastructure (Azure Bicep IaC)

This directory contains the Azure infrastructure definitions (Infrastructure as Code) for VedaAide.NET, written in Azure Bicep.

---

## Directory Structure

```
infra/
├── main.bicep              # Subscription-level entry: creates the resource group, calls modules
├── main.parameters.json    # Example deployment parameters (no sensitive information)
└── modules/
    └── container-apps.bicep # Container Apps + Log Analytics + Managed Identity
```

---

## Azure Resources Deployed

| Resource | Naming rule | Notes |
|------|----------|------|
| Resource Group | `rg-vedaaide` | Container for all resources |
| Log Analytics Workspace | `vedaaide-{env}-logs` | Central container-log storage |
| Container Apps Environment | `vedaaide-{env}-env` | Managed runtime environment |
| Container App | `vedaaide-{env}-api` | VedaAide API container, scales 0→3 elastically |
| User Assigned Managed Identity | `vedaaide-{env}-identity` | Passwordless access to CosmosDB / Azure OpenAI |

> The CosmosDB account and Azure OpenAI resource must be **created in advance** (usually managed independently from the business), passed in via Endpoint parameters.

---

## Quick Deploy

### Prerequisites

```bash
# Install Azure CLI
# https://docs.microsoft.com/cli/azure/install-azure-cli

az login
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
```

### 1. Copy and edit the parameters file

```bash
cp infra/main.parameters.json infra/main.parameters.local.json
# Edit main.parameters.local.json and fill in your resource endpoints and image address
```

**Do not commit a parameters file containing real endpoints to Git.**

### 2. Deploy the infrastructure

```bash
az deployment group create \
  --resource-group dev-dj-sbi-customer_group \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.local.json
```

Outputs:
- `apiUrl` — HTTPS access URL of the Container App
- `containerAppName` — used later by `az containerapp update`
- `identityPrincipalId` — needed for the next step's authorization

### 3. Authorize the Managed Identity

```bash
PRINCIPAL_ID="<identityPrincipalId from step 2 output>"
SUBSCRIPTION="<YOUR_SUBSCRIPTION_ID>"
RG="dev-dj-sbi-customer_group"

# Grant access to Azure OpenAI
az role assignment create \
  --assignee "$PRINCIPAL_ID" \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/$SUBSCRIPTION/resourceGroups/$RG/providers/Microsoft.CognitiveServices/accounts/<AOAI_ACCOUNT>"

# Grant access to CosmosDB (built-in Data Contributor)
az cosmosdb sql role assignment create \
  --account-name <COSMOS_ACCOUNT> \
  --resource-group "$RG" \
  --principal-id "$PRINCIPAL_ID" \
  --role-definition-id 00000000-0000-0000-0000-000000000002 \
  --scope "/"
```

### 4. Deploy the app (first time)

```bash
az containerapp update \
  --name vedaaide-dev-api \
  --resource-group dev-dj-sbi-customer_group \
  --image ghcr.io/YOUR_ORG/vedaaide-api:latest
```

Afterwards, pushing to the `main` branch updates it automatically via GitHub Actions.

---

## CI/CD Configuration (GitHub Actions)

See [.github/workflows/deploy.yml](../.github/workflows/deploy.yml).

The following Secrets/Variables must be configured in the GitHub repository:

| Type | Name | Value |
|------|------|----|
| Secret | `AZURE_CLIENT_ID` | App ID of the Federated Identity app registration |
| Secret | `AZURE_TENANT_ID` | Azure AD Tenant ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| Variable | `AZURE_RESOURCE_GROUP` | `dev-dj-sbi-customer_group` |
| Variable | `CONTAINER_APP_NAME` | `vedaaide-dev-api` |

Federated Identity configuration (lets GitHub Actions sign in to Azure without a password):

```bash
az ad app federated-credential create \
  --id <APP_OBJECT_ID> \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:YOUR_ORG/VedaAide.NET:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

---

## Environment Variable Reference

No code changes are needed in the Container App; all backend settings are configured via environment variables:

```bash
# Storage backend
Veda__StorageProvider=CosmosDb
Veda__CosmosDb__Endpoint=https://xxx.documents.azure.com:443/

# AI providers (Managed Identity mode: leave ApiKey empty)
Veda__EmbeddingProvider=AzureOpenAI
Veda__LlmProvider=AzureOpenAI
Veda__AzureOpenAI__Endpoint=https://xxx.openai.azure.com/

# Semantic cache
Veda__SemanticCache__Enabled=true
Veda__SemanticCache__TtlSeconds=3600

# Security (inject via Container Apps Secrets, never write in plain text env vars)
Veda__Security__ApiKey=<secretRef>
Veda__Security__AdminApiKey=<secretRef>
Veda__Security__AllowedOrigins=https://your-resume-site.com
```
