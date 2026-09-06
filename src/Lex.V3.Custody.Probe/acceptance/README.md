# Issue 459: custody reconciliation, 2026-09-06

This is execution evidence, not a product contract or production acceptance.
Writer: Codex; reviewer: Claude. Authority: `SFHAJJI/lex-governance` main
`37b6f5763c1b15778522e16296194efd27799cc2`, Stage 1 S1-A03/A04 and the
incorporated frozen #342 acceptance. Product base:
`627e068d268c51bcae1e83af3d6e6c970bf9d6d2`. Both remotes were fetched and
matched those identities. The containing Git commit freezes this evidence;
the issue's REVIEW REQUEST binds its exact head and tree.

The [pre-code reconciliation](https://github.com/SFHAJJI/lex/issues/459#issuecomment-5558127091)
was posted before product edits. The original Azure provider's
[Claude verdict](https://github.com/SFHAJJI/lex/pull/356#issuecomment-5488228596)
was opened at `fbd87ed2653a18a55872d4af23bbad96cf572dc6`; its deferred production
acceptance is still required. Historic test totals are not claimed here.

| Acceptance clause | Classification and disposition |
|---|---|
| Managed identity, no account keys or application secret | Implemented; retain public store/journal constructors and probe credential guards. Deployed identity and effective RBAC acceptance remain unaccepted. |
| Exact transport bytes, create-only addresses, read-back before decode | Implemented and previously accepted provider boundary; retain. Azure staging and generation checks, `BytesBeforeDecode`, and checked restore are covered by the personally run suite. |
| Effective retention from ARM, private journal, missing/below-floor refusal | Implemented; retain reader, journal and object-relative floor. Deployment is incorrect: the observed production `nightly` container has no immutability policy. |
| Restore and accepted-Fact replay | Restore by reference and digest is implemented; retain. The probe supports a fresh invocation. Complete production Fact-to-observation-to-bytes replay evidence is missing; a synthetic write/read is insufficient. |
| Rejected and partial evidence never becomes derived legal text | Implemented; retain routed HTTP custody and derivability boundaries. Current full suite exercises rejected responses, framing refusal, custody failure and checked restoration. |
| Distinct failure causes | Repair: ARM non-200 statuses were discarded. Preserve `HttpRequestException.StatusCode` inside the existing `CustodyRequiredException`; Blob SDK status causes, integrity, policy and cancellation handling remain intact. This is diagnostic distinction, not a new public refusal vocabulary. |
| Independently reviewed real-provider receipt | Missing. Local tests and CLI control-plane reads are bounded evidence only. No production write/read/restore/replay acceptance is claimed. |

## Personally executed evidence

Windows x64, SDK selected by `global.json`: `10.0.400` (`dotnet --info`).

| Command | Observed result |
|---|---|
| `dotnet restore Lex.V3.slnx --locked-mode` | Exit 0 at the base. No lockfile changes. |
| `dotnet build Lex.V3.slnx --configuration Release --no-restore` | Exit 0 at base and repaired source; 0 warnings, 0 errors. |
| `dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Release --no-restore --no-build --filter 'FullyQualifiedName~Custody' --minimum-expected-tests 1` | Base: 225 total, 224 passed, 1 skipped, 0 failed. |
| `dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~NonSuccessStatusPreservesTheCauseWithoutResponseContent' --minimum-expected-tests 6` | RED on base implementation plus new test: 6 failed because the status cause was null. GREEN after repair: 6 passed, none skipped. Cases: 401, 403, 404, 429, 500, 503. |
| `dotnet test --solution Lex.V3.slnx --configuration Release --no-restore --no-build --minimum-expected-tests 1` | Repaired source: 2632 total, 2629 passed, 3 skipped, 0 failed. |
| `dotnet list Lex.V3.slnx package --vulnerable --include-transitive --no-restore` | Exit 0; no known vulnerable packages reported by the configured sources. |
| `dotnet run --project src/Lex.V3.Custody.Probe/Lex.V3.Custody.Probe.csproj --configuration Release --no-build -- write nightly_floor_90d` | Local environment refusal: exit 1, stdout 0 bytes, stderr `custody_probe_failed`. No identity endpoint or custody configuration supplied. This is not a live provider write. |

Full-suite skips: `ALaneSymbolicLinkCannotRedirectCustodyOutsideItsLane`
(Windows permission unavailable),
`TheCensusFamiliesProveAndTheRunEitherReachesTheManifestOrFailsNamingWhy`
(opt-in EU live canary), and
`TheCodeCivilsTwoCanaryExpressionsAreHeldWithRealReceipts`
(opt-in LU live canary). No skipped outcome counts as a pass.

The six new cases prove the diagnostic assertion fails against the previous
implementation. They also assert that response content, synthetic bearer token
and the account host do not enter the exception text. Existing tests retain
the no-receipt outcomes for missing authoritative retention and protection
below ninety days. No new admission guard or relaxation was introduced.

## Openable control-plane evidence and external blocker

[`2026-09-06-control-plane.json`](2026-09-06-control-plane.json) contains the
personally executed `az rest` commands and parsed responses for the exact
`2025-06-01` API used by the provider. SHA-256:
`8c85f885c8d223afd458ee86b1647eadf03f29e47e7263c2954e96092a2bb85c`.
It is CLI operator evidence, not raw HTTP or managed-identity execution evidence.

Observed resource:
`/subscriptions/7c1e98f3-ef6e-45a0-b61b-a51d436bf093/resourceGroups/rg-lex-v3-custody/providers/Microsoft.Storage/storageAccounts/stlexv3custody`.

* `nightly`: `hasImmutabilityPolicy=false`, `hasLegalHold=false`, no
  `immutabilityPolicy` member in the REST response. The CLI container model
  separately rendered that absence as null. There is no measured retention floor.
* `legal-hold`: active legal hold, protected block appends false, private access.
* `staging`: private, no hold or immutability policy.
* All three report version-level immutability disabled.

`az containerapp job list` found no Lex job in the current subscription.
`az containerapp list` found `ca-lex-web` using `uami-lex-runtime`; it is the
production web application, not an established custody acceptance runner.
`az identity list` returned existing Lex identities, but this does not establish
their effective custody RBAC. The local environment name census found no
`LEX_V3_CUSTODY_*`, `IDENTITY_*` or `AZURE_*` variables. These observations do not
prove that no runner exists in another service or subscription.

To unblock production acceptance:

1. Approve and apply a container-level locked time policy on the exact `nightly`
   resource, with protected appends disabled and enough duration that each fresh
   object still has at least 90 days remaining at the ARM observation. A proposed
   91-day policy allows write/read-back latency; the per-object guard remains
   decisive. Locking is irreversible; no policy or role was changed in this run.
2. Establish a bounded Azure-host runner with an explicitly selected user-assigned
   identity, verified data/control-plane permissions and opaque lane policy keys.
   Any new spending and irreversible external changes require owner approval under
   governance work-model clause 13.1. Do not use account keys or a developer token
   as the provider's identity, or repurpose production web traffic.
3. Run the exact reviewed probe image for both lanes, retain configuration journal
   evidence, restore receipts in fresh invocations, and execute complete
   accepted-Fact replay through retained observations. Enumerate that population;
   neither an unobserved denominator nor synthetic bytes proves it complete.

The [Microsoft .NET constructor contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httprequestexception.-ctor?view=net-10.0)
supports retaining a status without response text. Microsoft's
[immutable storage guidance](https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview)
explains locked retention. These describe platform behavior, not Lex authority.

No Azure mutation, production promotion, signing, release artifact, or merge was
performed. Stage 1 remains open. Claude's exact-head review and required CI remain
separate gates. Issue #421 had no current REVIEW REQUEST at the local test boundary.
