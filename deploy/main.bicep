targetScope = 'resourceGroup'

// The one-server Lex V3 container: the API serving its live pages and its mounted corpus, exactly as the
// release image holds them (STATUS, "The release pipeline's image steps"). Deployed only by deploy.ps1, and only
// when the owner decides to go live: production credentials, deployment and promotion are the owner's.
//
// The template holds no secret and reads none. The image is pulled with the user-assigned managed identity the owner
// names (no registry password), and the app runs with no environment secret. The app keeps every revision it deploys
// (multiple revision mode): a new revision arrives as the `candidate` label with no traffic while a live revision
// serves, so the zero-traffic probe runs against the candidate's own URL before anything is promoted. On a first
// deployment there is no live revision to keep the traffic, so the candidate takes it, and ingress admits only the
// owner's probe address until the owner promotes the app by redeploying without that restriction.

@description('Azure region of the app; the managed environment\'s by default.')
param location string = resourceGroup().location

@description('The existing Container Apps managed environment, by resource id.')
param environmentId string

@description('The container app\'s name.')
param appName string = 'ca-lex-v3'

@description('The release image by digest, <registry>/<repository>@sha256:<64 lower-case hex>. deploy.ps1 refuses a tag.')
param image string

@description('The registry server the image is pulled from with the managed identity.')
param registryServer string

@description('The user-assigned managed identity the app pulls its image with, by resource id. No secret is stored.')
param identityResourceId string

@description('The revision suffix naming the release: lower-case letters, digits and hyphens.')
param revisionSuffix string

@description('The revision serving traffic now, kept at 100 per cent; empty on the first deployment.')
param liveRevision string = ''

@description('On a first deployment only: the one address range ingress admits (the owner\'s probe), as a CIDR.')
param probeSourceCidr string = ''

var firstDeployment = empty(liveRevision)

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityResourceId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        traffic: firstDeployment
          ? [
              {
                latestRevision: true
                weight: 100
                label: 'candidate'
              }
            ]
          : [
              {
                revisionName: liveRevision
                weight: 100
              }
              {
                latestRevision: true
                weight: 0
                label: 'candidate'
              }
            ]
        ipSecurityRestrictions: firstDeployment && !empty(probeSourceCidr)
          ? [
              {
                name: 'owner-probe-only'
                description: 'First deployment: only the owner\'s probe reaches the candidate until promotion.'
                ipAddressRange: probeSourceCidr
                action: 'Allow'
              }
            ]
          : []
      }
      registries: [
        {
          server: registryServer
          identity: identityResourceId
        }
      ]
    }
    template: {
      revisionSuffix: revisionSuffix
      containers: [
        {
          name: 'lex-v3'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/evaluation-card.json'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
          // Mounting a corpus verifies its index into a private temporary file, so the container needs a writable,
          // private /tmp (the image run's finding, PR #825); nothing else of its filesystem is written.
          volumeMounts: [
            {
              volumeName: 'tmp'
              mountPath: '/tmp'
            }
          ]
        }
      ]
      // One server: the mount and its generations open at startup into its own /tmp.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'tmp'
          storageType: 'EmptyDir'
        }
      ]
    }
  }
}

@description('The revision this deployment created.')
output candidateRevision string = app.properties.latestRevisionName

@description('The app\'s host name; the candidate label\'s host is <app>---candidate.<environment domain>.')
output fqdn string = app.properties.configuration.ingress.fqdn
