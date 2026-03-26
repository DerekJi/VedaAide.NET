# VedaAide 基础设施（Azure Bicep IaC）

本目录包含 VedaAide.NET 的 Azure 基础设施定义（Infrastructure as Code），使用 Azure Bicep 编写。

---

## 目录结构

```
infra/
├── main.bicep              # 订阅级入口：创建资源组，调用模块
├── main.parameters.json    # 部署参数示例（不含敏感信息）
└── modules/
    └── container-apps.bicep # Container Apps + Log Analytics + Managed Identity
```

---

## 部署的 Azure 资源

| 资源 | 名称规则 | 说明 |
|------|----------|------|
| Resource Group | `rg-vedaaide` | 所有资源的容器 |
| Log Analytics Workspace | `vedaaide-{env}-logs` | 容器日志集中存储 |
| Container Apps Environment | `vedaaide-{env}-env` | 托管运行时环境 |
| Container App | `vedaaide-{env}-api` | VedaAide API 容器，支持 0→3 弹性伸缩 |
| User Assigned Managed Identity | `vedaaide-{env}-identity` | 免密访问 CosmosDB / Azure OpenAI |

> CosmosDB 账户和 Azure OpenAI 资源需**预先创建**（通常与业务独立管理），通过 Endpoint 参数传入。

---

## 快速部署

### 前置条件

```bash
# 安装 Azure CLI
# https://docs.microsoft.com/cli/azure/install-azure-cli

az login
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
```

### 1. 复制并修改参数文件

```bash
cp infra/main.parameters.json infra/main.parameters.local.json
# 编辑 main.parameters.local.json，填入你的资源端点和镜像地址
```

**不要将包含真实端点的参数文件提交到 Git。**

### 2. 部署基础设施

```bash
az deployment group create \
  --resource-group dev-dj-sbi-customer_group \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.local.json
```

输出：
- `apiUrl` — Container App 的 HTTPS 访问地址
- `containerAppName` — 后续 `az containerapp update` 使用
- `identityPrincipalId` — 下一步授权所需

### 3. 授权 Managed Identity

```bash
PRINCIPAL_ID="<identityPrincipalId from step 2 output>"
SUBSCRIPTION="<YOUR_SUBSCRIPTION_ID>"
RG="dev-dj-sbi-customer_group"

# 授权访问 Azure OpenAI
az role assignment create \
  --assignee "$PRINCIPAL_ID" \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/$SUBSCRIPTION/resourceGroups/$RG/providers/Microsoft.CognitiveServices/accounts/<AOAI_ACCOUNT>"

# 授权访问 CosmosDB（内置 Data Contributor）
az cosmosdb sql role assignment create \
  --account-name <COSMOS_ACCOUNT> \
  --resource-group "$RG" \
  --principal-id "$PRINCIPAL_ID" \
  --role-definition-id 00000000-0000-0000-0000-000000000002 \
  --scope "/"
```

### 4. 部署应用（首次）

```bash
az containerapp update \
  --name vedaaide-dev-api \
  --resource-group dev-dj-sbi-customer_group \
  --image ghcr.io/YOUR_ORG/vedaaide-api:latest
```

后续推送到 `main` 分支后 GitHub Actions 会自动更新。

---

## CI/CD 配置（GitHub Actions）

参见 [.github/workflows/deploy.yml](../.github/workflows/deploy.yml)。

需在 GitHub 仓库配置以下 Secrets/Variables：

| 类型 | 名称 | 值 |
|------|------|----|
| Secret | `AZURE_CLIENT_ID` | Federated Identity 应用注册的 App ID |
| Secret | `AZURE_TENANT_ID` | Azure AD Tenant ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | Azure 订阅 ID |
| Variable | `AZURE_RESOURCE_GROUP` | `dev-dj-sbi-customer_group` |
| Variable | `CONTAINER_APP_NAME` | `vedaaide-dev-api` |

Federated Identity 配置（允许 GitHub Actions 免密登录 Azure）：

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

## 环境变量参考

Container App 中无需修改代码，通过环境变量配置所有后端：

```bash
# 存储后端
Veda__StorageProvider=CosmosDb
Veda__CosmosDb__Endpoint=https://xxx.documents.azure.com:443/

# AI 提供商（Managed Identity 模式：ApiKey 留空）
Veda__EmbeddingProvider=AzureOpenAI
Veda__LlmProvider=AzureOpenAI
Veda__AzureOpenAI__Endpoint=https://xxx.openai.azure.com/

# 语义缓存
Veda__SemanticCache__Enabled=true
Veda__SemanticCache__TtlSeconds=3600

# 安全（通过 Container Apps Secrets 注入，不要明文写在环境变量）
Veda__Security__ApiKey=<secretRef>
Veda__Security__AdminApiKey=<secretRef>
Veda__Security__AllowedOrigins=https://your-resume-site.com
```
