# Stage 2 Deployment Planning

**Question 1: Will pushing the current code to `main` deploy automatically?**

**No — a few one-time prerequisites are still missing**, but the pipeline code itself is complete. Once configured, **every push afterwards runs the whole flow automatically**.

---

**Question 2: What you need to provide/configure**

Do it in three phases, in order:

---

### Phase 1: Manually create two resources in Azure (Bicep does not cover these)

Bicep only creates the Container Apps environment. You must create the following two resources yourself first:

| Resource | Reason |
|------|------|
| **Azure OpenAI resource** + two model deployments (`text-embedding-3-small`, `gpt-4o-mini`) | Model access requests require manual approval, not suitable for IaC automation |
| **CosmosDB for NoSQL Serverless account** + two containers (`VectorChunks`, `SemanticCache`, vector search must be enabled) | Same as above; recommended to manage separately |

After creation, note down the two **Endpoint URLs** (shaped like `https://xxx.openai.azure.com/` and `https://xxx.documents.azure.com:443/`).

```
https://dev-dj-open-ai.openai.azure.com/

https://vedaaide.documents.azure.com:443/
```

---

### Phase 2: Deploy the Bicep infrastructure (one-time)

```bash
# 1. Copy and fill in the parameter file (do NOT commit this file)
cp infra/main.parameters.json infra/main.parameters.local.json
# Edit and fill in: azureOpenAiEndpoint, cosmosDbEndpoint, containerImage, allowedOrigins

# 2. Deploy
az login
az account set --subscription "<subscription ID>"
az deployment group create \
  --resource-group dev-dj-sbi-customer_group \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.local.json

# 3. Record the identityPrincipalId from the output; needed in the next step
```

Then grant permissions to the Managed Identity (**three commands**, see README.md):
- `Cognitive Services OpenAI User` → Azure OpenAI
- `Cosmos DB Built-in Data Contributor` → CosmosDB
- `Storage Blob Data Reader` → Blob (if using an Azure Blob data source)

---

### Phase 3: Configure the GitHub repository (5 values)

Set these in **Settings → Secrets and variables → Actions**:

| Type | Name | Value source |
|------|------|--------|
| Secret | `AZURE_CLIENT_ID` | App ID of the OIDC App Registration below |
| Secret | `AZURE_TENANT_ID` | Azure AD tenant ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| Variable | `AZURE_RESOURCE_GROUP` | `dev-dj-sbi-customer_group` |
| Variable | `CONTAINER_APP_NAME` | `vedaaide-dev-api` |

Also create a **GitHub Environment** named `production` (Settings → Environments → New).

**OIDC federated identity (replaces passwords/secrets)**:
```bash
# Create the App Registration
az ad app create --display-name "vedaaide-github-actions"
# Record the appId (that is AZURE_CLIENT_ID) and id (APP_OBJECT_ID) from the output

# Add a federated credential (lets GitHub sign in to Azure without a password)
az ad app federated-credential create \
  --id <APP_OBJECT_ID> \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:YOUR_ORG/VedaAide.NET:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Assign the Contributor role to this App (for Container Apps deployment)
az role assignment create \
  --assignee <appId> \
  --role Contributor \
  --scope /subscriptions/<subscription ID>/resourceGroups/dev-dj-sbi-customer_group
```

---

### Flow After Configuration

```
git push → main
    ↓ ~2 minutes
  Build + Test (dotnet)
    ↓ on success
  Docker build → push to GHCR
    ↓
  az containerapp update (image updated automatically)
    ↓ done within ~8-10 minutes
```

> **Need me to run any of these steps for you?** Or, if you already have an Azure OpenAI / CosmosDB account, you can start directly from Phase 2.

### Verification Steps
1. The pipeline itself: check the GitHub Actions logs

https://github.com/derekji/VedaAide.NET/actions → click the latest run → all three jobs green:

Build & Test ✅
Publish Docker Image ✅
Deploy to Azure Container Apps ✅

2. Whether the container is running: Health Check

```bash
curl https://vedaaide-dev-api.graymeadow-b0bfbc64.australiaeast.azurecontainerapps.io/health
```

Expected response: Healthy

(Cold start takes about 15-30 seconds the first time; retry a few times on timeout)

3. Whether the API is usable: Admin Stats

```bash
curl https://vedaaide-dev-api.graymeadow-b0bfbc64.australiaeast.azurecontainerapps.io/api/admin/stats
```

Expected response similar to: {"chunkCount":0,"documentCount":0,"syncedFileCount":0}

(AdminApiKey is not set yet, so no Header is needed)

4. Whether CosmosDB containers are created automatically

Go to Azure Portal → CosmosDB vedaaide → Data Explorer; you should see the VectorChunks and SemanticCache containers under the VedaAide database.

5. Quick end-to-end: ingest + query

```bash
BASE="https://vedaaide-dev-api.graymeadow-b0bfbc64.australiaeast.azurecontainerapps.io"

# Ingest a test document
curl -X POST "$BASE/api/documents/ingest" \
  -H "Content-Type: application/json" \
  -d '{"content":"VedaAide is a RAG system built with .NET 10.","documentName":"test.md","documentType":"Note"}'

# Wait a few seconds, then query
curl -X POST "$BASE/api/query" \
  -H "Content-Type: application/json" \
  -d '{"question":"What is VedaAide?"}'
```
