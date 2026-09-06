# Production custody runner proposal

Issue #459, S1-A04. This is a deployment proposal and personally executed preparation evidence,
not production acceptance. The existing provider and probe program are unchanged from accepted
integration `e305791e66f82fef17dfed2da2185d68a3607385`.

## Owner decision, after independent review

Approve the following bounded operation, conditional on Claude's exact-head READY and required CI:

1. Upload the already inspected development OCI image to the existing private ACR repository
   `crsoufien3orem.azurecr.io/lex-v3-custody-probe`. Use the archive, preserving manifest digest
   `sha256:c3289ea3396620150b9cd1a64b20b985cfb649303e01c2450afafbec747ec78b`.
2. Deploy `main.bicep` incrementally to existing `rg-lex-v3-custody`. It creates the dedicated
   `uami-lex-v3-custody-probe`, two custom role definitions and three container-scoped assignments,
   an AcrPull assignment on the existing registry, and manual job `caj-lex-v3-custody-probe` in
   existing `rg-platform/cae-platform-law`. It changes no storage policies and starts no job.
3. Before changing retention, execute one `write nightly_floor_90d` probe and retain the refusal,
   execution metadata, selected control-plane evidence and logs. Failure alone does not identify
   its cause; distinguish missing retention from identity/authorization/availability problems.
   Stop on any unexpected failure instead of retrying or widening permissions.
4. Create an unlocked **91-day** container-level policy on exactly `stlexv3custody/nightly`, with
   both protected-append flags false. Read it back. Lock that exact ETag only after confirming
   account, lane, duration, no legal hold on this lane and the owner approval. Retain the before,
   unlocked and locked responses. Locking cannot be undone or shortened; protected objects can
   retain storage cost for the duration. The provider still requires at least 90 days remaining
   for each observed object. This proposal does not touch the separate `legal-hold` policy.
5. Execute one write per lane and then one fresh job execution per receipt using `read-receipt`.
   Each write creates 32 random synthetic bytes plus the existing private configuration journal.
   Preserve the exact image digest, execution IDs/statuses, output receipt bytes and journal refs.
   This proves only provider write/read-back/restore. It does not prove accepted-Fact replay.

Bound: **five manual executions maximum**, each one replica, zero retries, 300-second timeout,
0.25 vCPU and 0.5 GiB. This bounds requested workload to 375 vCPU-seconds and 750 GiB-seconds,
not a monetary quote. Existing environment/registry/logging and storage pricing still apply;
no free-grant availability is assumed. There is no schedule, event trigger or ingress. Any
unexpected failure stops this operation; extra runs, permission changes or resources need a
revised concrete plan. New spending and the irreversible lock require owner approval under
[work-model 13.1](https://github.com/SFHAJJI/lex-governance/blob/37b6f5763c1b15778522e16296194efd27799cc2/work-model/WORKING-MODEL-CANDIDATE-5-2026-09-05.md).

## Reconciliation and permissions

- Retain: managed-identity-only provider and journal, create-only content addressing, staging
  cleanup, exact digest/length reads, effective ARM policy checks and receipt restore command.
- Verify: real Azure managed-identity execution, effective role propagation, both custody lanes,
  private configuration journal, fresh-process restore and retention refusal.
- Missing: a selected, bounded hosted runner and its deployment coordinates. These templates
  provide that proposal. They do not modify `ca-lex-web` or reuse its runtime identity.
- Still missing: a complete accepted-Fact population and replay receipt. Synthetic probe bytes,
  a zero population or a successful infrastructure deployment cannot satisfy that obligation.

The destination custom role grants ARM `containers/read` and data-plane `blobs/read` and
`blobs/write`. It is assigned only to `nightly` and `legal-hold`. The staging role adds
`blobs/delete` and is assigned only to `staging`. Neither grants container/property/policy
writes, account keys, delegation keys or role assignment. Private journal blobs use the same
lane permissions and WORM lifetime. The provider's authenticated server-side copy uses its
managed-identity source authorization; no SAS/delegation-key grant is added.

An initial draft used Storage Blob Data Contributor. Directly reading that built-in role showed
it also grants container write/delete; it was replaced before deployment. The current custom
roles preserve the provider's required operations without those configuration permissions.
The exact public Azure storage endpoint remains enforced by `AzureBlobCustodyOptions`.

## Personally executed preparation

Evidence files are in `evidence/`; the large local development image is under
`artifacts/issue-459-runner/custody-probe.tar` in the writer worktree and is not Git-tracked or
registry-published. Its receipt records every archive member and application assembly digest. The raw publication
command and tool output are retained; no full-suite rerun is claimed for deployment-only files.
A rebuilt image must be inspected again and its exact digest reviewed; the build timestamp
means reproducing the source is not a claim to reproduce this OCI digest.

- Locked restore and `dotnet publish ... /t:PublishContainer` succeeded using SDK 10.0.400 and
  the already pinned V3 `aspnet:10.0-noble-chiseled` base. No Docker daemon was needed.
- Opened the OCI archive: 10 members hash-verified, three application assemblies byte-identical
  to the published files, Linux/amd64, user 1654, entrypoint `dotnet /app/Lex.V3.Custody.Probe.dll`.
  Archive: 57,573,888 bytes, SHA-256
  `bac8df7f280bcf61ac54fdf01bc4244fb16682136ab1a4e8ab82862e94023af4`.
  The SDK emitted an uncompressed tar despite the initial `.tar.gz` filename; it was renamed
  to `.tar` without changing bytes.
- Executed the published managed assembly on Windows with missing identity: exit 1, stdout
  zero bytes, stderr exactly `custody_probe_failed`. This is not Linux/container execution.
- ORAS 1.3.4 opened the local archive and independently returned the pinned manifest digest.
  Its portable Windows asset matched upstream SHA-256
  `ffdb6aa40267686b5d507da1f21a57fc502a9a7c86b90c54557d335644c99dbd`.
- Bicep compiled without warnings after resolving the `environment` symbol qualification.
  Azure read-only template validation succeeded. Changing the image to `:latest` was rejected
  by Azure with `InvalidTemplate` at `parameters.image.allowedValues` (exit 1).
- Final read-only what-if succeeded and enumerated eight creates: two roles, one identity, three
  container role assignments, one job and one registry pull assignment. Every other reported
  resource was Ignore; no Modify/Delete was reported. The first draft could not enumerate the
  pull assignment because its name depended on the uncreated principal ID; naming it from the
  known identity resource ID made that assignment independently visible in what-if.
- Nine read-only Azure observations retained the existing environment, empty job inventory in
  `rg-platform`, publisher identity and role list, registry, storage network settings and three
  containers. These are parsed CLI operator observations, not provider identity evidence.
- Separate direct ARM GETs returned HTTP 200 for all three exact containers, with ETag, Date and
  request ID headers. Raw bodies and selected headers were retained; each body digest/length
  and body/header ETag matched. The existing provider's header guard needs no repair.

## Commands prepared for the approved execution

These are **not reported as executed**. Run them only at the reviewed head after approval,
re-fetching the exact resource state and inspecting the new what-if result first. The subscription
is `7c1e98f3-ef6e-45a0-b61b-a51d436bf093`; verify it explicitly in the operator context.

Upload the retained archive with ORAS, using an ephemeral Entra ACR token on standard input and
an isolated registry credential file; never log the token or pass it in a command argument.
The selected operator must already have push authority. Do not enable or use ACR admin keys.

```powershell
# Login preparation is operator-only; the application still uses its dedicated managed identity.
$acrSession = az acr login --name crsoufien3orem --expose-token --output json | ConvertFrom-Json
$acrSession.accessToken | & $oras login crsoufien3orem.azurecr.io --username 00000000-0000-0000-0000-000000000000 --password-stdin --registry-config $temporaryRegistryConfig
& $oras cp --from-oci-layout 'artifacts/issue-459-runner/custody-probe.tar:e305791e66f82fef17dfed2da2185d68a3607385' 'crsoufien3orem.azurecr.io/lex-v3-custody-probe:e305791e66f82fef17dfed2da2185d68a3607385' --to-registry-config $temporaryRegistryConfig
& $oras manifest fetch 'crsoufien3orem.azurecr.io/lex-v3-custody-probe@sha256:c3289ea3396620150b9cd1a64b20b985cfb649303e01c2450afafbec747ec78b' --descriptor --registry-config $temporaryRegistryConfig
# Clear the in-memory token and remove only that exact temporary credential file in finally.

az deployment group create --resource-group rg-lex-v3-custody --name lex-v3-custody-probe --template-file src/Lex.V3.Custody.Probe/deployment/main.bicep --mode Incremental
az containerapp job start --resource-group rg-platform --name caj-lex-v3-custody-probe --container-name probe --args write nightly_floor_90d
```

The first execution is the pre-lock refusal. Capture its ID, effective identity, exact image,
state/exit status and complete logs. Read the data/control-plane evidence before proceeding.
The following is the separate irreversible operation, after the explicit owner decision:

```powershell
az storage container immutability-policy create --resource-group rg-lex-v3-custody --account-name stlexv3custody --container-name nightly --period 91 --allow-protected-append-writes false --allow-protected-append-writes-all false
# Re-read and retain the exact policy; verify all conditions above before using its returned ETag.
az storage container immutability-policy lock --resource-group rg-lex-v3-custody --account-name stlexv3custody --container-name nightly --if-match $verifiedPolicyEtag
```

After verifying the locked policy and managed-identity roles, start the two write executions
(one per exact lane, `nightly_floor_90d` and `legal_hold_evidence`). Retain each portable receipt's
original JSON bytes, parse it under the existing contract, and check the expected lane/policy key.
Encode those exact receipt bytes as base64url without padding; start a **new execution** with
`--args read-receipt $receiptBase64Url`. The probe's existing parser bounds and validates this input.
Do not replace this with a second read in the original write process. Success for reads is exit 0;
there is no receipt printed by read mode. Link the input receipt to the specific read execution.

The private configuration journal stays under the protected lane's `_configuration/v1/` prefix.
Retain its exact records privately and publish only the portable receipt and agreed evidence
projection. Preserve failed/partial execution evidence. Do not label any of this an accepted-Fact
replay until a separately enumerated non-synthetic population resolves through observations to
those exact retained bytes.

Stop and remove/revoke only the newly created runner resources and role assignments if the
approved operation is cancelled. Do not attempt to undo locked retention or clear legal holds;
those bytes remain under the approved lifetime. No V3 release or rollback lineage is created.

Platform references: [jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs),
[managed identity](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity),
[container retention](https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-policy-configure-container-scope),
[SDK OCI export](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish), and
[ORAS archive copying](https://oras.land/docs/how_to_guides/distributing_oci_layouts/).
These describe platform behavior; lex-governance remains the authority.
