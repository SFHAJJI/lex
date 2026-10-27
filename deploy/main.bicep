targetScope = 'resourceGroup'

// The one-server Lex V3 container: the API serving its live pages and its mounted corpus, exactly as the
// release image holds them (STATUS, "The release pipeline's image steps"). Deployed only by deploy.ps1, and only
// when the owner decides to go live: production credentials, deployment and promotion are the owner's.
//
// The template holds no secret and reads none. The image is pulled with the user-assigned managed identity the owner
// names (no registry password), and the app runs with no environment secret. The app keeps every revision it deploys
// (multiple revision mode): a new revision arrives as the `candidate` label with no traffic while a live revision
// serves, so the zero-traffic probe runs against the candidate's own URL before anything is promoted. On a first
// deployment there is no live revision to keep the traffic, so the candidate takes it.
//
// Until the owner opens the site, the app is the private copy: on every deployment, ingress admits only the address
// ranges deploy.ps1 names (the operator's, and a reviewer's while a review runs), and a deployment that names none admits
// no one. Only the owner's opening (deploy.ps1 -Open, then -Public on every later deployment) admits everyone. (The plan's
// item 0.5: the first version applied the list on a first deployment only, so a second deployment opened the copy.)
//
// A release's maintenance revision (deploy.ps1 -Maintenance; R60, R108, R115; the plan's item 10.14) is the same image with
// LEX_V3_MAINTENANCE=on: it mounts nothing and serves only the maintenance page. It arrives as the `maintenance` label with
// no traffic beside the live revision, which keeps 100 per cent, is probed and kept inactive, and is switched on by moving
// the traffic to it (the owner's move, as a promotion is). It is never a first deployment.

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

@description('Until the opening: the address ranges ingress admits, as CIDRs. A deployment that names none admits no one.')
param allowedSourceCidrs array = []

@description('True only once the owner has opened the site (deploy.ps1 -Public): ingress then admits everyone.')
param openToPublic bool = false

@description('Whether search engines may index the site, read by the API at startup as LEX_V3_SEARCH_ENGINES: kept-out (every page noindex, no sitemap) or indexed (the owner\'s answer to O27 on 2026-10-18; law pages still open only for the works the release\'s indexing record lists).')
@allowed([
  'kept-out'
  'indexed'
])
param searchEngines string = 'kept-out'

@description('The custom domains bound to the app, as Azure holds them (name, bindingType, certificateId): deploy.ps1 reads them back and passes them on every deployment, so a redeployment keeps the binding. Empty until law.soufien.lu is bound (deploy/bind-domain.ps1).')
param customDomains array = []

@description('True for the release\'s maintenance revision (deploy.ps1 -Maintenance; R60, R108, R115): the same image with LEX_V3_MAINTENANCE=on, serving only the maintenance page, deployed with no traffic beside the live revision, which keeps 100 per cent.')
param maintenance bool = false

var firstDeployment = empty(liveRevision)

// A revision that holds the law: the API listens only once its mount is open and verified (32 s on one core over the
// population mount), so a startup probe gives it about four and a half minutes (restarted at the tenth failure, 10 + 9 x
// 30 s) before the liveness probe, which would otherwise restart it, begins. Ready only while it holds the law: the API
// answers 503 at /health/ready when no corpus is mounted (plan 0.7), so a replica whose mount was refused takes no traffic,
// and monitoring can ask the same route.
var releaseProbes = [
  {
    type: 'Startup'
    httpGet: {
      path: '/'
      port: 8080
    }
    initialDelaySeconds: 10
    periodSeconds: 30
    failureThreshold: 10
  }
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
      path: '/health/ready'
      port: 8080
    }
    initialDelaySeconds: 5
    periodSeconds: 10
  }
]

// A maintenance revision mounts nothing, so it listens within seconds; every page of it, `/` included, answers 503 (the
// maintenance page), so all three probes ask its readiness, which is 204 only while it holds a maintenance page in each
// interface language: one without them is never ready, and its deployment fails.
var maintenanceProbes = [
  {
    type: 'Startup'
    httpGet: {
      path: '/health/ready'
      port: 8080
    }
    initialDelaySeconds: 2
    periodSeconds: 5
    failureThreshold: 10
  }
  {
    type: 'Liveness'
    httpGet: {
      path: '/health/ready'
      port: 8080
    }
    initialDelaySeconds: 2
    periodSeconds: 30
  }
  {
    type: 'Readiness'
    httpGet: {
      path: '/health/ready'
      port: 8080
    }
    initialDelaySeconds: 2
    periodSeconds: 10
  }
]

// Every address range named, each admitted; ingress refuses every other address.
var allowedOnly = [
  for (cidr, index) in allowedSourceCidrs: {
    name: 'private-copy-${index + 1}'
    description: 'The private copy: only the named addresses reach it until the owner opens the site.'
    ipAddressRange: cidr
    action: 'Allow'
  }
]

// With no range named, one that is never routed (TEST-NET-1, RFC 5737): a deployment that forgets the list is closed, not
// open.
var admitNoOne = [
  {
    name: 'private-copy-closed'
    description: 'No address range was named: the private copy admits no one.'
    ipAddressRange: '192.0.2.1/32'
    action: 'Allow'
  }
]

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
        // A maintenance revision takes no traffic as it deploys, whatever else is named: with no live revision the template
        // names an empty one and Azure refuses the deployment (deploy.ps1 refuses it first, before anything changes).
        traffic: maintenance
          ? [
              {
                revisionName: liveRevision
                weight: 100
              }
              {
                latestRevision: true
                weight: 0
                label: 'maintenance'
              }
            ]
          : (firstDeployment
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
                ])
        ipSecurityRestrictions: openToPublic ? [] : (empty(allowedSourceCidrs) ? admitNoOne : allowedOnly)
        customDomains: customDomains
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
          // One vCPU and 2 GiB, measured over the combined population mount (2026-10-13, STATUS-WEB.md): the API opens it with
          // a 0.5 GB peak, and its private /tmp holds 2.15 GB of verified index copies, more than the 2 GiB of ephemeral
          // storage a half-vCPU replica has (one vCPU has 4 GiB).
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            // The replica's /tmp is this container's alone and outlives a restart of it: the API removes the index copies an
            // earlier start left there before it mounts again (V3StaleIndexCopies).
            {
              name: 'LEX_V3_TEMP_IS_PRIVATE'
              value: '1'
            }
            // Search engines (3.6): only the exact value `indexed` opens the pages the indexing record lists.
            {
              name: 'LEX_V3_SEARCH_ENGINES'
              value: searchEngines
            }
            // The maintenance revision (R60): only the exact value `on` makes the API mount nothing and serve the maintenance
            // page alone (V3WebRoot.Maintenance.cs); every other revision says `off`.
            {
              name: 'LEX_V3_MAINTENANCE'
              value: maintenance ? 'on' : 'off'
            }
          ]
          probes: maintenance ? maintenanceProbes : releaseProbes
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
