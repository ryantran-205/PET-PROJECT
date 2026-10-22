// =====================================================================================
// Brewline F&B — Azure infrastructure (Container Apps hướng chính). KHÔNG tự deploy.
// Xem docs/deployment.md. Lần đầu: deployApps=false (tạo ACR/KV/SQL/...), nạp secrets vào
// Key Vault, push images, rồi deploy lại với deployApps=true.
// Mọi danh tính dùng chung một User-Assigned Managed Identity (đơn giản cho MVP; có thể tách
// identity riêng cho api/worker/migrator khi cần least-privilege chặt hơn).
// =====================================================================================
targetScope = 'resourceGroup'

@description('Môi trường triển khai.')
@allowed(['development', 'staging', 'production'])
param environmentName string

@description('Tiền tố tên tài nguyên (chữ thường, 3-10 ký tự).')
@minLength(3)
@maxLength(10)
param namePrefix string

param location string = resourceGroup().location

@description('Tag image trong ACR (fnb-api, fnb-worker, fnb-web).')
param imageTag string = 'latest'

@description('false ở lần chạy đầu tiên khi ACR chưa có image.')
param deployApps bool = true

@description('Tên secret trong Key Vault — phải được tạo thủ công trước khi deployApps=true.')
param jwtSecretName string = 'jwt-signing-key'
param trackingSecretName string = 'tracking-token-secret'

var isProd = environmentName == 'production'
var suffix = uniqueString(resourceGroup().id, environmentName)
var base = '${namePrefix}-${environmentName}'
var aspnetEnv = isProd ? 'Production' : 'Staging'
var tags = {
  app: 'brewline-fnb'
  environment: environmentName
}

// ---------------- Observability ----------------
resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${base}-law'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: isProd ? 90 : 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${base}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: law.id
  }
}

// ---------------- Identity ----------------
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${base}-id'
  location: location
  tags: tags
}

// ---------------- Container Registry ----------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: toLower(replace('${namePrefix}${environmentName}${suffix}', '-', ''))
  location: location
  tags: tags
  sku: { name: isProd ? 'Standard' : 'Basic' }
  properties: { adminUserEnabled: false }
}

// ---------------- Key Vault ----------------
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${namePrefix}-${take(environmentName, 4)}-${suffix}', 24)
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: isProd ? true : null
  }
}

// ---------------- Azure SQL (Entra-only auth) ----------------
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${base}-sql-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: uami.name
      sid: uami.properties.principalId
      tenantId: tenant().tenantId
    }
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'fnb'
  location: location
  tags: tags
  sku: isProd ? { name: 'GP_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 } : { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 1 }
  properties: {
    autoPauseDelay: isProd ? null : 60
    minCapacity: isProd ? null : json('0.5')
    requestedBackupStorageRedundancy: isProd ? 'Geo' : 'Local'
    zoneRedundant: false
  }
}

resource dbShortTermBackup 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01-preview' = {
  parent: db
  name: 'default'
  properties: { retentionDays: isProd ? 35 : 7 }
}

// ---------------- Redis (cache + revocation) ----------------
// Kiểm tra lại SKU/dịch vụ khả dụng (Azure Cache for Redis vs Azure Managed Redis) tại thời điểm triển khai.
resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${base}-redis-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: isProd ? { name: 'Standard', family: 'C', capacity: 1 } : { name: 'Basic', family: 'C', capacity: 0 }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    redisVersion: '6'
  }
}

resource redisSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'redis-connection'
  properties: {
    value: '${redis.properties.hostName}:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

// ---------------- Service Bus ----------------
resource sb 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${base}-sb-${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: sb
  name: 'order-events'
  properties: {
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    defaultMessageTimeToLive: 'P7D'
  }
}

resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [for s in ['order-processing', 'realtime']: {
  parent: topic
  name: s
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
  }
}]

// ---------------- SignalR Service (scale-out) ----------------
resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: '${base}-sigr-${suffix}'
  location: location
  tags: tags
  sku: isProd ? { name: 'Standard_S1', tier: 'Standard', capacity: 1 } : { name: 'Free_F1', tier: 'Free', capacity: 1 }
  kind: 'SignalR'
  properties: {
    features: [{ flag: 'ServiceMode', value: 'Default' }]
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

// ---------------- RBAC cho managed identity ----------------
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  kvSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  sbSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  sbReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  signalrAppServer: '420fcaa2-552c-430f-98ca-3264be4806c7'
}

resource raAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, uami.id, roles.acrPull)
  properties: { principalId: uami.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull) }
}

resource raKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, uami.id, roles.kvSecretsUser)
  properties: { principalId: uami.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.kvSecretsUser) }
}

resource raSbSend 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sb
  name: guid(sb.id, uami.id, roles.sbSender)
  properties: { principalId: uami.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.sbSender) }
}

resource raSbRecv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sb
  name: guid(sb.id, uami.id, roles.sbReceiver)
  properties: { principalId: uami.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.sbReceiver) }
}

resource raSignalr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: signalr
  name: guid(signalr.id, uami.id, roles.signalrAppServer)
  properties: { principalId: uami.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.signalrAppServer) }
}

// ---------------- Container Apps ----------------
resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${base}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
  }
}

var apiAppName = '${base}-api'
var webAppName = '${base}-web'
var webOrigin = 'https://${webAppName}.${cae.properties.defaultDomain}'
var registry = acr.properties.loginServer

var kvSecrets = [
  { name: 'sql-connection', value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${db.name};Authentication=Active Directory Managed Identity;User Id=${uami.properties.clientId};Encrypt=True;' }
  { name: 'redis-connection', keyVaultUrl: '${kv.properties.vaultUri}secrets/redis-connection', identity: uami.id }
  { name: 'jwt-signing-key', keyVaultUrl: '${kv.properties.vaultUri}secrets/${jwtSecretName}', identity: uami.id }
  { name: 'tracking-token-secret', keyVaultUrl: '${kv.properties.vaultUri}secrets/${trackingSecretName}', identity: uami.id }
]

var backendEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: aspnetEnv }
  { name: 'AZURE_CLIENT_ID', value: uami.properties.clientId }
  { name: 'ConnectionStrings__Sql', secretRef: 'sql-connection' }
  { name: 'ConnectionStrings__Redis', secretRef: 'redis-connection' }
  { name: 'Messaging__Provider', value: 'AzureServiceBus' }
  { name: 'Messaging__AzureServiceBus__FullyQualifiedNamespace', value: '${sb.name}.servicebus.windows.net' }
  { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
  { name: 'Ordering__TrackingTokenSecret', secretRef: 'tracking-token-secret' }
  { name: 'Ordering__PublicBaseUrl', value: webOrigin }
  { name: 'Cors__AllowedOrigins__0', value: webOrigin }
  { name: 'Azure__SignalR__ConnectionString', value: 'Endpoint=https://${signalr.properties.hostName};AuthType=azure.msi;ClientId=${uami.properties.clientId};Version=1.0;' }
  { name: 'Hosting__EnableInProcessWorker', value: 'false' }
  { name: 'Database__MigrateOnStartup', value: 'false' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
]

var identityBlock = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${uami.id}': {} }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: apiAppName
  location: location
  tags: tags
  identity: identityBlock
  dependsOn: [raAcr, raKv, raSbSend, raSbRecv, raSignalr, redisSecret]
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      // API chỉ nhận traffic nội bộ từ web (nginx reverse proxy) → không lộ trực tiếp ra Internet.
      ingress: { external: false, targetPort: 8080, transport: 'auto', allowInsecure: true }
      registries: [{ server: registry, identity: uami.id }]
      secrets: kvSecrets
    }
    template: {
      containers: [{
        name: 'api'
        image: '${registry}/fnb-api:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: backendEnv
        probes: [
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 15 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 10, failureThreshold: 3 }
        ]
      }]
      scale: {
        minReplicas: isProd ? 2 : 1
        maxReplicas: isProd ? 6 : 2
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: '${base}-worker'
  location: location
  tags: tags
  identity: identityBlock
  dependsOn: [raAcr, raKv, raSbSend, raSbRecv, raSignalr, redisSecret]
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      registries: [{ server: registry, identity: uami.id }]
      secrets: kvSecrets
    }
    template: {
      containers: [{
        name: 'worker'
        image: '${registry}/fnb-worker:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: backendEnv
      }]
      scale: { minReplicas: 1, maxReplicas: isProd ? 3 : 1 }
    }
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: webAppName
  location: location
  tags: tags
  identity: identityBlock
  dependsOn: [raAcr]
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: { external: true, targetPort: 80, transport: 'auto', allowInsecure: false }
      registries: [{ server: registry, identity: uami.id }]
    }
    template: {
      containers: [{
        name: 'web'
        image: '${registry}/fnb-web:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: [{ name: 'API_UPSTREAM', value: 'http://${apiAppName}' }]
        probes: [{ type: 'Liveness', httpGet: { path: '/', port: 80 }, periodSeconds: 30 }]
      }]
      scale: { minReplicas: isProd ? 2 : 1, maxReplicas: isProd ? 4 : 1 }
    }
  }
}

// Migration: Container Apps Job chạy thủ công (`az containerapp job start`), image API với --migrate-only.
resource migrateJob 'Microsoft.App/jobs@2024-03-01' = if (deployApps) {
  name: '${base}-migrate'
  location: location
  tags: tags
  identity: identityBlock
  dependsOn: [raAcr, raKv, redisSecret]
  properties: {
    environmentId: cae.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registry, identity: uami.id }]
      secrets: kvSecrets
    }
    template: {
      containers: [{
        name: 'migrate'
        image: '${registry}/fnb-api:${imageTag}'
        args: ['--migrate-only']
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: backendEnv
      }]
    }
  }
}

output acrLoginServer string = registry
output keyVaultName string = kv.name
output webUrl string = webOrigin
output sqlServer string = sql.properties.fullyQualifiedDomainName
output serviceBusNamespace string = sb.name
output managedIdentityClientId string = uami.properties.clientId
