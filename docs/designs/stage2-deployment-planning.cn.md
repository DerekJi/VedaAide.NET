**问题 1：当前代码推送到 `main` 能否自动部署？**

**不能，还差几步前置配置**，但 pipeline 代码本身是完整的，准备好后**一次性配置，之后每次推送自动走完**。

---

**问题 2：需要你提供/设置的内容**

分三个阶段，按顺序做：

---

### 阶段一：在 Azure 手动创建两个资源（Bicep 不负责）

Bicep 只创建 Container Apps 环境，以下两个资源需要你自己先建好：

| 资源 | 原因 |
|------|------|
| **Azure OpenAI 资源** + 两个模型部署（`text-embedding-3-small`、`gpt-4o-mini`） | 模型申请需人工审批，不适合 IaC 自动化 |
| **CosmosDB for NoSQL Serverless 账户** + 两个容器（`VectorChunks`、`SemanticCache`，需开启向量搜索） | 同上，建议独立管理 |

建好后记录两个 **Endpoint URL**（形如 `https://xxx.openai.azure.com/` 和 `https://xxx.documents.azure.com:443/`）。

```
https://dev-dj-open-ai.openai.azure.com/

https://vedaaide.documents.azure.com:443/
```

---

### 阶段二：部署 Bicep 基础设施（一次性）

```bash
# 1. 复制并填写参数文件（不要提交这个文件）
cp infra/main.parameters.json infra/main.parameters.local.json
# 编辑填入：azureOpenAiEndpoint, cosmosDbEndpoint, containerImage, allowedOrigins

# 2. 部署
az login
az account set --subscription "<订阅ID>"
az deployment group create \
  --resource-group dev-dj-sbi-customer_group \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.local.json

# 3. 记录输出中的 identityPrincipalId，下一步用
```

然后给 Managed Identity 授权（**三条命令**，详见 README.md）：
- `Cognitive Services OpenAI User` → Azure OpenAI
- `Cosmos DB Built-in Data Contributor` → CosmosDB
- `Storage Blob Data Reader` → Blob（如用 Azure Blob 数据源）

---

### 阶段三：配置 GitHub 仓库（5 个值）

在仓库 **Settings → Secrets and variables → Actions** 中设置：

| 类型 | 名称 | 值来源 |
|------|------|--------|
| Secret | `AZURE_CLIENT_ID` | 下方 OIDC App Registration 的 App ID |
| Secret | `AZURE_TENANT_ID` | Azure AD 租户 ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | 订阅 ID |
| Variable | `AZURE_RESOURCE_GROUP` | `dev-dj-sbi-customer_group` |
| Variable | `CONTAINER_APP_NAME` | `vedaaide-dev-api` |

还需创建一个 **GitHub Environment** 名为 `production`（Settings → Environments → New）。

**OIDC 联合身份（替代密码/密钥）**：
```bash
# 创建 App Registration
az ad app create --display-name "vedaaide-github-actions"
# 记录输出的 appId（即 AZURE_CLIENT_ID）和 id（APP_OBJECT_ID）

# 添加联合凭据（允许 GitHub 免密登录 Azure）
az ad app federated-credential create \
  --id <APP_OBJECT_ID> \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:YOUR_ORG/VedaAide.NET:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# 给这个 App 赋 Contributor 角色（用于 Container Apps 部署）
az role assignment create \
  --assignee <appId> \
  --role Contributor \
  --scope /subscriptions/<订阅ID>/resourceGroups/dev-dj-sbi-customer_group
```

---

### 配置完成后的流程

```
git push → main
    ↓ ~2分钟
  Build + Test (dotnet)
    ↓ 通过
  Docker build → push to GHCR
    ↓
  az containerapp update (自动更新镜像)
    ↓ 完成 ~8-10分钟内
```

> **需要我帮你执行其中哪个步骤吗？** 或者你已有 Azure OpenAI / CosmosDB 账户，可以直接从阶段二开始。


### 验证步骤
1. Pipeline 本身：查看 GitHub Actions 日志

https://github.com/derekji/VedaAide.NET/actions → 点最新一次 run → 三个 job 全绿即可：

Build & Test ✅
Publish Docker Image ✅
Deploy to Azure Container Apps ✅

2. 容器是否跑起来：Health Check

```bash
curl https://vedaaide-dev-api.graymeadow-b0bfbc64.australiaeast.azurecontainerapps.io/health
```

期望返回：Healthy

（首次冷启动约 15-30 秒，若超时多试几次）

3. API 是否可用：Admin Stats

```bash
curl https://vedaaide-dev-api.graymeadow-b0bfbc64.australiaeast.azurecontainerapps.io/api/admin/stats
```

期望返回类似：{"chunkCount":0,"documentCount":0,"syncedFileCount":0}

（当前未设置 AdminApiKey，所以无需 Header）

4. CosmosDB 容器是否自动创建

访问 Azure Portal → CosmosDB vedaaide → Data Explorer，应该能看到 VedaAide 数据库下的 VectorChunks 和 SemanticCache 两个容器。

5. 快速端到端：ingest + query

```bash
BASE="https://vedaaide-dev-api.graymeadow-b0bfbc64.australiaeast.azurecontainerapps.io"

# Ingest 一条测试文档
curl -X POST "$BASE/api/documents/ingest" \
  -H "Content-Type: application/json" \
  -d '{"content":"VedaAide is a RAG system built with .NET 10.","documentName":"test.md","documentType":"Note"}'

# 等几秒后查询
curl -X POST "$BASE/api/query" \
  -H "Content-Type: application/json" \
  -d '{"question":"What is VedaAide?"}'
```
