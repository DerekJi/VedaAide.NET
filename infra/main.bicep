// VedaAide Azure Container Apps 基础设施
// 部署方式：az deployment group create --resource-group dev-dj-sbi-customer_group --template-file main.bicep --parameters @main.parameters.json

targetScope = 'resourceGroup'

@description('部署区域')
param location string = 'australiaeast'

@description('应用环境标识（dev / staging / prod）')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Container Apps 镜像地址（如 ghcr.io/org/vedaaide-api:latest）')
param containerImage string

@description('Azure OpenAI 端点（使用 Managed Identity 时必填，ApiKey 留空）')
param azureOpenAiEndpoint string = ''

@description('CosmosDB 账户端点（使用 Managed Identity 时必填，AccountKey 留空）')
param cosmosDbEndpoint string = ''

@description('VedaAide API Key（留空则禁用认证，仅限开发）')
@secure()
param apiKey string = ''

@description('VedaAide Admin API Key')
@secure()
param adminApiKey string = ''

@description('允许跨域的来源，逗号分隔或 *')
param allowedOrigins string = '*'

// ── Modules ──────────────────────────────────────────────────────────────────
module infra 'modules/container-apps.bicep' = {
  name: 'vedaaide-infra'
  params: {
    location: location
    environment: environment
    containerImage: containerImage
    azureOpenAiEndpoint: azureOpenAiEndpoint
    cosmosDbEndpoint: cosmosDbEndpoint
    apiKey: apiKey
    adminApiKey: adminApiKey
    allowedOrigins: allowedOrigins
  }
}

output apiUrl string = infra.outputs.apiUrl
output containerAppName string = infra.outputs.containerAppName
output docIntelligenceEndpoint string = infra.outputs.docIntelligenceEndpoint
