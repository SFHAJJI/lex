param location string
param environmentName string
param jobName string
param image string
param registryServer string
param identityResourceId string
param clientId string
param storageAccountName string
param storageResourceGroup string
param subscriptionId string
param nightlyPolicyKey string
param legalHoldPolicyKey string

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: environmentName
}

resource job 'Microsoft.App/jobs@2025-07-01' = {
  name: jobName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityResourceId}': {} }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 300
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registryServer, identity: identityResourceId }]
    }
    template: {
      containers: [{
        name: 'probe'
        image: image
        args: ['write', 'nightly_floor_90d']
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: [
          { name: 'LEX_V3_CUSTODY_SERVICE_URI', value: 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}' }
          { name: 'LEX_V3_CUSTODY_STAGING_CONTAINER', value: 'staging' }
          { name: 'LEX_V3_CUSTODY_NIGHTLY_CONTAINER', value: 'nightly' }
          { name: 'LEX_V3_CUSTODY_LEGAL_HOLD_CONTAINER', value: 'legal-hold' }
          { name: 'LEX_V3_CUSTODY_MANAGED_IDENTITY_CLIENT_ID', value: clientId }
          { name: 'LEX_V3_CUSTODY_NIGHTLY_POLICY_KEY', value: nightlyPolicyKey }
          { name: 'LEX_V3_CUSTODY_LEGAL_HOLD_POLICY_KEY', value: legalHoldPolicyKey }
          { name: 'LEX_V3_CUSTODY_SUBSCRIPTION_ID', value: subscriptionId }
          { name: 'LEX_V3_CUSTODY_RESOURCE_GROUP', value: storageResourceGroup }
        ]
      }]
    }
  }
}

output resourceId string = job.id
