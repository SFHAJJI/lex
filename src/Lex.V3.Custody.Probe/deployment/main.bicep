targetScope = 'resourceGroup'

// Deploy to existing rg-lex-v3-custody only after the owner approves the concrete plan.
// This template deliberately contains no retention-policy mutation or job execution.
param location string = 'francecentral'
param identityName string = 'uami-lex-v3-custody-probe'
param storageAccountName string = 'stlexv3custody'
param platformResourceGroup string = 'rg-platform'
param environmentName string = 'cae-platform-law'
param registryResourceGroup string = 'rg-soufien-portfolio'
param registryName string = 'crsoufien3orem'
param jobName string = 'caj-lex-v3-custody-probe'

@description('Exact locally inspected OCI manifest; replace only with a newly reviewed image receipt.')
@allowed([
  'crsoufien3orem.azurecr.io/lex-v3-custody-probe@sha256:c3289ea3396620150b9cd1a64b20b985cfb649303e01c2450afafbec747ec78b'
])
param image string = 'crsoufien3orem.azurecr.io/lex-v3-custody-probe@sha256:c3289ea3396620150b9cd1a64b20b985cfb649303e01c2450afafbec747ec78b'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource account 'Microsoft.Storage/storageAccounts@2025-06-01' existing = {
  name: storageAccountName
}

var containers = ['staging', 'nightly', 'legal-hold']
resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-06-01' existing = [for name in containers: {
  name: '${account.name}/default/${name}'
}]

// The built-in Data Contributor also grants container writes. These roles grant only
// the provider's operations: ARM container GET, blob read/write, and staging cleanup.
var roleKinds = ['destination', 'staging']
resource role 'Microsoft.Authorization/roleDefinitions@2022-04-01' = [for kind in roleKinds: {
  name: guid(resourceGroup().id, 'lex-v3-custody-probe', kind)
  properties: {
    roleName: 'Lex V3 custody probe ${kind}'
    description: 'Bounded custody probe data access; no policy, container or account mutation.'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [{
      actions: ['Microsoft.Storage/storageAccounts/blobServices/containers/read']
      notActions: []
      dataActions: concat([
        'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'
        'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'
      ], kind == 'staging' ? ['Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete'] : [])
      notDataActions: []
    }]
  }
}]

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (name, i) in containers: {
  name: guid(container[i].id, identity.id, 'bounded-data-access')
  scope: container[i]
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: role[name == 'staging' ? 1 : 0].id
  }
}]

module pull './registry-pull.bicep' = {
  name: 'lex-v3-custody-probe-pull'
  scope: resourceGroup(registryResourceGroup)
  params: {
    registryName: registryName
    identityResourceId: identity.id
    principalId: identity.properties.principalId
  }
}

module job './job.bicep' = {
  name: 'lex-v3-custody-probe-job'
  scope: resourceGroup(platformResourceGroup)
  params: {
    location: location
    environmentName: environmentName
    jobName: jobName
    image: image
    registryServer: '${registryName}.azurecr.io'
    identityResourceId: identity.id
    clientId: identity.properties.clientId
    storageAccountName: storageAccountName
    storageResourceGroup: resourceGroup().name
    subscriptionId: subscription().subscriptionId
    // Stable opaque lane identities; private journal binds them to the actual ARM coordinates.
    nightlyPolicyKey: guid(account.id, 'lex-v3-custody-probe', 'nightly')
    legalHoldPolicyKey: guid(account.id, 'lex-v3-custody-probe', 'legal-hold')
  }
  dependsOn: [blobRole, pull]
}

output identityResourceId string = identity.id
output identityClientId string = identity.properties.clientId
output jobResourceId string = job.outputs.resourceId
