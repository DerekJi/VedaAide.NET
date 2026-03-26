// VedaAide - Azure Container Apps + 关联服务
// 包含：Log Analytics、Container Apps Environment、Container App（含 Managed Identity）

@description('部署区域')
param location string

@description('环境标识')
param environment string

@description('容器镜像地址')
param containerImage string

@description('Azure OpenAI 端点')
param azureOpenAiEndpoint string

@description('CosmosDB 端点')
param cosmosDbEndpoint string

@secure()
param apiKey string

@secure()
param adminApiKey string

param allowedOrigins string

var prefix = 'vedaaide-${environment}'

// ── Log Analytics Workspace ──────────────────────────────────────────────────
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Container Apps Environment ────────────────────────────────────────────────
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ── User-Assigned Managed Identity ───────────────────────────────────────────
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: '${prefix}-identity'
  location: location
}

// ── Container App ─────────────────────────────────────────────────────────────
resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      secrets: !empty(apiKey) || !empty(adminApiKey) ? [
        ...(!empty(apiKey)      ? [{ name: 'api-key',       value: apiKey      }] : [])
        ...(!empty(adminApiKey) ? [{ name: 'admin-api-key', value: adminApiKey }] : [])
      ] : []
    }
    template: {
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'http-scale'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
      containers: [
        {
          name: 'vedaaide-api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            // ── 存储后端 ──────────────────────────────────────────────────────
            { name: 'Veda__StorageProvider',   value: 'CosmosDb' }
            { name: 'Veda__CosmosDb__Endpoint', value: cosmosDbEndpoint }
            // AccountKey 留空 → 使用 Managed Identity

            // ── AI 提供商 ──────────────────────────────────────────────────────
            { name: 'Veda__EmbeddingProvider', value: 'AzureOpenAI' }
            { name: 'Veda__LlmProvider',       value: 'AzureOpenAI' }
            { name: 'Veda__AzureOpenAI__Endpoint', value: azureOpenAiEndpoint }
            // ApiKey 留空 → 使用 Managed Identity

            // ── 安全 ────────────────────────────────────────────────────────────
            ...(!empty(apiKey)      ? [{ name: 'Veda__Security__ApiKey',      secretRef: 'api-key'       }] : [])
            ...(!empty(adminApiKey) ? [{ name: 'Veda__Security__AdminApiKey', secretRef: 'admin-api-key' }] : [])
            { name: 'Veda__Security__AllowedOrigins', value: allowedOrigins    }

            // ── Managed Identity client ID ────────────────────────────────────
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
          ]
        }
      ]
    }
  }
}

output apiUrl string = 'https://${app.properties.configuration.ingress!.fqdn}'
output containerAppName string = app.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
