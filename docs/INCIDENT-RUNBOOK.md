# Lex incident runbook

What to do when https://law.soufien.lu is down, wrong or stale, with the real commands (the specification of record's
R108; the operating rules around it are in [OPERATING.md](OPERATING.md)). Every command runs in PowerShell 7 from a
checkout of this repository, signed in with `az login` as an identity that may change `rg-lex-v3`.

The names used below:

| What | Name |
| --- | --- |
| Resource group | `rg-lex-v3` (France Central) |
| Container app | `ca-lex-v3`, one replica of 1 vCPU and 2 GiB per active revision, multiple-revision mode |
| A revision | `ca-lex-v3--<release version, lower case>`, for example `ca-lex-v3--v3-20261026t180319z-8958ec5fa63f` |
| Environment | `cae-lex-v3` |
| Site | `law.soufien.lu`, a CNAME to the app's default host, with a managed certificate |
| Release signer | `lex-v3-release`; its public key is `src/Lex.V3.Api/well-known/lex-v3-release.pem`, published at `/.well-known/lex-keys.json` |

## 1. Classes, and who acts

- **S1: down, or serving wrong legal text.** Act at once (section 3), then tell the owner.
- **S2: degraded or stale** (slow, a screen failing, data past its 30 days, the certificate within 21 days of its end).
  Act the same day.
- **S3: cosmetic.** A normal pull request.

R108 pre-authorises three actions, done at once and reported: moving traffic back to the previous signed V3 revision; a
static maintenance page linking Legilux and EUR-Lex; blocking an abusive client. O17 asks the owner to adopt that; until
the owner does, whoever operates still takes these three at once for an S1 and reports them in the same hour, as on
2026-10-27. Everything else (a new release, a hotfix, any change of money, credentials or public legal wording) needs the
owner's sign-off. V2 is never a rollback target (R115). A runtime-only hotfix over an unchanged signed index needs a
numbered exception (Reg63(c)).

## 2. The first five minutes: what is wrong

```powershell
# The monitor's own checks, from here: readiness, home page, a dated reading page, one API operation, the certificate.
node web/scripts/site-monitor.mjs --second-look-ms 0

# Which revisions exist, which are active, and where the traffic goes.
az containerapp revision list -g rg-lex-v3 -n ca-lex-v3 `
  --query "[].{name:name, active:properties.active, traffic:properties.trafficWeight, health:properties.healthState, running:properties.runningState, created:properties.createdTime}" -o table
az containerapp ingress traffic show -g rg-lex-v3 -n ca-lex-v3 -o table

# The live revision's replica and its console (startup lines only: the server logs nothing about requests).
az containerapp replica list -g rg-lex-v3 -n ca-lex-v3 --revision <live revision> -o table
az containerapp logs show -g rg-lex-v3 -n ca-lex-v3 --revision <live revision> --tail 50
```

The outside monitor (`.github/workflows/v3-monitor.yml`) runs the same checks every five minutes from GitHub's runners and
opens the issue "Lex monitor: law.soufien.lu failed its outside check" after a check fails twice; it closes the issue once
the site answers again.

## 3. Actions

Paste each block whole and read what it says before the next one. PowerShell does not stop when a command such as `az`
fails, which is how a failed step went on to the next on 2026-10-27; so every block below is one `& { ... }` that checks
each command's exit code and stops at the first failure, saying so, and every step that switches something off reads the
traffic back first and acts only on what it reads. The marked blocks are run under a made-up Azure CLI by
`web/test/runbook-blocks.test.mjs`, so they cannot drift from these rules unseen.

### Move the traffic: a rollback, a forward move, a promotion

The same three blocks serve all three. A deactivated revision is kept and can be activated again; it needs about a minute
to mount its index before it is ready.

First, name the two revisions (a rollback sends the traffic to the previous revision; a promotion to the candidate):

<!-- runbook:names -->
```powershell
$rg = 'rg-lex-v3'; $app = 'ca-lex-v3'
$target = '<the revision that is to serve: for a rollback the previous one, for a promotion the candidate>'
$from = '<the revision serving now>'
```

1. Activate the target, unless it is active already (a candidate is):

<!-- runbook:activate -->
```powershell
& {
  az containerapp revision activate -g $rg -n $app --revision $target
  if ($LASTEXITCODE -ne 0) { Write-Host "Activating $target failed: stop here; nothing else was changed."; return }
  Write-Host "$target is activated; the next block waits until it is ready."
}
```

2. Wait until the target is running and healthy (ten minutes at most), move all traffic to it, and read the traffic back:

<!-- runbook:move -->
```powershell
& {
  $deadline = (Get-Date).AddMinutes(10)
  while ($true) {
    $state = az containerapp revision show -g $rg -n $app --revision $target `
      --query "{active:properties.active, running:properties.runningState, health:properties.healthState}" -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { Write-Host "$target could not be read: stop here; no traffic moved."; return }
    if ($state.active -eq $true -and "$($state.running)".StartsWith('Running') -and $state.health -eq 'Healthy') { break }
    if ((Get-Date) -gt $deadline) { Write-Host "$target is not running and healthy after ten minutes ($($state.running), $($state.health)): stop here; no traffic moved."; return }
    Write-Host "waiting: $target is $($state.running), $($state.health)"
    Start-Sleep -Seconds 15
  }
  az containerapp ingress traffic set -g $rg -n $app --revision-weight "$target=100"
  if ($LASTEXITCODE -ne 0) { Write-Host "Moving the traffic to $target failed: stop here, and read the traffic (section 2) before anything else."; return }
  $traffic = az containerapp ingress traffic show -g $rg -n $app -o json | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { Write-Host "The traffic could not be read back: stop here."; return }
  $onTarget = [int](($traffic | Where-Object { $_.revisionName -eq $target } | Measure-Object -Property weight -Sum).Sum)
  if ($onTarget -ne 100) { Write-Host "The traffic reads $onTarget per cent on ${target}: stop here."; return }
  Write-Host "All traffic is on $target. Check the site: node web/scripts/site-monitor.mjs --second-look-ms 0"
}
```

3. Then, so one replica is billed, deactivate the revision that served before, and only once the traffic reads 100 per
cent on the target and none on it:

<!-- runbook:deactivate -->
```powershell
& {
  $traffic = az containerapp ingress traffic show -g $rg -n $app -o json | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { Write-Host "The traffic could not be read: stop here; nothing deactivated."; return }
  $onTarget = [int](($traffic | Where-Object { $_.revisionName -eq $target } | Measure-Object -Property weight -Sum).Sum)
  $onFrom = [int](($traffic | Where-Object { $_.revisionName -eq $from } | Measure-Object -Property weight -Sum).Sum)
  if ($onTarget -ne 100 -or $onFrom -ne 0) { Write-Host "The traffic is not all on $target ($onTarget per cent, and $onFrom on $from): stop here; nothing deactivated."; return }
  az containerapp revision deactivate -g $rg -n $app --revision $from
  if ($LASTEXITCODE -ne 0) { Write-Host "Deactivating $from failed: it stays active and billed; nothing else changed."; return }
  Write-Host "$from is deactivated: it is kept, and can be activated again."
}
```

**Bring back a live revision that was deactivated by mistake** (the incident of 2026-10-27, section 4): name it as
`$target`, then run blocks 1 and 2; block 3 is not needed. Readiness answers 204 once it holds the law:
``curl.exe -s -o NUL -w "%{http_code}`n" https://law.soufien.lu/health/ready``.

### Deploy a release, and promote it

An owner's decision, unless it is a rollback. Every deployment is `deploy/deploy.ps1`. It verifies the release under the
signing key, deploys it as a zero-traffic candidate, probes the candidate at its own address and deactivates it if the
probe fails; promotion is printed, never run. Since the opening, every deployment says `-Public`. Its rules since #992 and
#993:
- each deployment makes a revision of its own, `ca-lex-v3--<the release's suffix>-<8 hex digits of its settings>` (a
  SHA-256 over the image, `-SearchEngines`, `-Public`, the address ranges and `deploy/main.bicep`), so the same release
  with another setting deploys as a new revision;
- a redeployment must name the revision serving now with `-LiveRevision`, and it must be one of the app's: without it the
  kit refuses before anything changes;
- a revision that already exists (the same release with the same settings) is refused before anything changes: one that
  exists is activated and probed by hand (below), never deployed again;
- the kit deactivates only a revision this deployment made;
- the probe is handed `-SearchEngines` as `--search-engines`, and holds robots.txt and the sitemap to it;
- promotion refuses (exit 3, no promotion command printed) a release whose data is past the maximum age, or whose signed
  evaluation card has a gate that did not pass, a control that missed its shuffle, or a target that is not the release's
  mount (R104).

<!-- runbook:deploy -->
```powershell
& {
  $subscription = az account show --query id -o tsv
  if ($LASTEXITCODE -ne 0) { Write-Host 'Not signed in: az login first.'; return }
  $environment = az containerapp env show -g rg-lex-v3 -n cae-lex-v3 --query id -o tsv
  $registry = az acr list -g rg-lex-v3 --query "[0].loginServer" -o tsv
  $identity = az identity list -g rg-lex-v3 --query "[0].id" -o tsv
  $live = az containerapp revision list -g rg-lex-v3 -n ca-lex-v3 --query "[?properties.trafficWeight==``100``].name | [0]" -o tsv
  if ($LASTEXITCODE -ne 0 -or -not $live) { Write-Host 'The revision serving now could not be read: stop here.'; return }
  pwsh -File deploy/deploy.ps1 -Subscription $subscription -ResourceGroup rg-lex-v3 -AppName ca-lex-v3 `
    -EnvironmentId $environment -Registry $registry -IdentityResourceId $identity `
    -SigningPublicKey src/Lex.V3.Api/well-known/lex-v3-release.pem -SignerIdentity lex-v3-release `
    -Release '<release directory>' -LiveRevision $live -Public -SearchEngines indexed -Apply
  if ($LASTEXITCODE -ne 0) { Write-Host 'The deployment did not pass: there is nothing to promote.'; return }
}
```

**Probe a revision that exists, by hand** (one the kit refuses to deploy again): name it as `$target`, activate it
(block 1), then

```powershell
$fqdn = az containerapp revision show -g rg-lex-v3 -n ca-lex-v3 --revision $target --query properties.fqdn -o tsv
node web/scripts/deploy-probe.mjs --origin "https://$fqdn" --release '<release directory>' `
  --public-key src/Lex.V3.Api/well-known/lex-v3-release.pem --signer lex-v3-release --promotion-check --search-engines indexed
```

**Before promoting: the limit counts each reader apart** (the review of #995, F3). The server's tests cannot see
Container Apps' ingress, so whether it forwards each reader's own address, which the per-client limit keys on, is checked
on the candidate itself, from two machines with different public addresses (this PC, and for example Azure Cloud Shell or
a phone's connection), within one minute. From the first, spend its minute at the candidate's own address (in
`deploy.ps1`'s output, or `az containerapp revision show ... --query properties.fqdn`):

<!-- runbook:limit-first -->
```powershell
& {
  $origin = 'https://<the candidate''s own address>'
  $body = '{"operation_id":"coverage","parameters":{}}'
  $statuses = foreach ($i in 1..150) {
    (Invoke-WebRequest -Method Post -Uri "$origin/api/v3/coverage" -ContentType 'application/json' -Body $body -SkipHttpErrorCheck).StatusCode
  }
  $limited = @($statuses | Where-Object { $_ -eq 429 }).Count
  Write-Host "200: $(@($statuses | Where-Object { $_ -eq 200 }).Count), 429: $limited"
  if ($limited -eq 0) { Write-Host 'No 429 from this address: the limit counts no one behind this ingress. Do not promote.'; return }
  Write-Host 'This address is limited. Now, within the minute, ask once from the second machine.'
}
```

Then, at once, from the second machine (bash, as in Cloud Shell):

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST -H 'content-type: application/json' \
  --data '{"operation_id":"coverage","parameters":{}}' 'https://<the candidate own address>/api/v3/coverage'
```

It must print 200. A 429 there means the ingress shows every reader as one address, so the limit would be one bucket for
the whole site: do not promote, and reopen the limit's design. The candidate has its own replica and its own count, so
the check spends nothing of the live site's readers.

**Promotion** (the owner's): blocks 2 and 3 above, with `$target` the candidate and `$from` the revision serving now.

### Other actions

**Block an abusive client** (S1 or S2). The per-client limit already answers a client past 60 API requests a minute
with `rate_limited` and 429; a block is for a flood that the limit does not stop. Container Apps applies either allow
rules or deny rules; the open site has none, so deny rules block only what they name.

```powershell
az containerapp ingress access-restriction set -g rg-lex-v3 -n ca-lex-v3 --rule-name block-1 --ip-address <CIDR> --action Deny
az containerapp ingress access-restriction list -g rg-lex-v3 -n ca-lex-v3 -o table
# Remove it as soon as the flood ends, and always before a deployment: deploy.ps1 -Public stops while any rule exists.
az containerapp ingress access-restriction remove -g rg-lex-v3 -n ca-lex-v3 --rule-name block-1
```

**Maintenance page.** The build writes one per language (`web/dist-live/maintenance/fr.html`, `en.html`), but no
maintenance revision exists yet (the plan's item 10.14), and no fallback outside Azure exists either (R117). Until they
do, the answer to an S1 is a rollback.

## 4. Known failure modes

| Failure | What it looks like | What to do |
| --- | --- | --- |
| **2026-10-27, about 01:10 to 01:13 UTC: a redeploy deactivated the live revision.** The release already live was redeployed to change only `LEX_V3_SEARCH_ENGINES`. `deploy.ps1` names the revision after the release, so Azure refused it ("revision with suffix ... already exists"), and the kit's failure handler then deactivated the revision of that name, the live one. The site answered nothing for about three minutes. | The site down right after a deployment that failed at Azure Resource Manager. | Reactivate the live revision (section 3, blocks 1 and 2), check readiness. Fixed by #992: each deployment names a revision of its own (the release's suffix and a digest of its settings), a collision is refused before anything is written, a redeployment must name `-LiveRevision`, and the kit never deactivates a revision it did not create. The runbook's own blocks now stop at a failed step and read the traffic back before switching anything off (section 3). |
| A candidate never becomes ready | The deployment fails at Azure's progress deadline; `/health/ready` on the candidate answers 503 (no law mounted). | The kit deactivates it; no traffic moved. Read its replica's console for the mount's refusal. |
| The probe fails | `deploy.ps1` stops with "failed its probe". | The kit deactivated the candidate; the live revision is untouched. |
| Promotion refused for the data's age | The probe exits 3: a publisher's newest completed collection is older than 30 days. | No promotion command is printed; a release with newer data is the way forward (OPERATING.md, "Freshness"). |
| Promotion refused for the evaluation card (R104) | The probe exits 3 and says "promotion refuses the release: the evaluation card's ... gate ... is ..., not pass", a control that missed its shuffle, or a card run over another mount. | No promotion command is printed. A release whose card passes over its own mount is the way forward; the only override R104 allows is the owner's written break-glass waiver for a harness defect. |
| The release path's checks refuse a release (R106, R86(2)) | `image-rehearsal.mjs` stops before building, naming each unexplained change against the release serving now, or each admitted consolidation that yields no text. | Read `release-comparison.json` and `consolidation-text.json` in its work directory. A change that is meant gets a typed reason in the release's explanations file, listed in the pull request; anything else is a defect to fix before releasing. Nothing was built or published. |
| Data past 30 days on the live site | Every page shows the staleness banner; the monitor's `api` check fails ("stale"). | S2: a refresh run (needs the owner's per-run yes), a derive and a release. |
| The certificate near its end | The monitor's `certificate` check fails within 21 days of the end. | The managed certificate renews itself while `law` stays a CNAME to the app; check the binding with `az containerapp hostname list -g rg-lex-v3 -n ca-lex-v3 -o table`, or rebind with `deploy/bind-domain.ps1`. |
| The limit sees no client, or one client for everyone | After a platform change the ingress might stop forwarding each reader's own address: the limit would then count no one, or make one bucket for the whole site. | The two-address check of section 3 ("Before promoting"), against a candidate; the server's own tests cannot see the ingress. |
| One office, one address | Many readers behind one address share one budget of 60 API requests a minute. | Raise `V3ClientLimiter.ApiPerMinute` in a release if it happens (R109: "re-sized by the load test"). |
| A derive job stopped by the platform | An Azure Flex job execution ends with "Stopping container" after waiting for a new node. | Start it again on the warm node (OPERATING.md, "Releases and data refreshes"). |
| The PC's disk full during a release build | The image step fails with no space left. | Free space (merged worktrees, old rehearsal folders) and build again from a clean checkout. |
| The subscription stops paying | Azure disables the subscription and everything in it, the DNS zone included (V2 went down this way). | The owner keeps a valid payment method; the trial credit ends on 2026-11-12. The DNS zone inside the subscription is an accepted risk (R117, the owner's choice of 2026-10-18). |
| The monitor itself stops | No run for a day. | GitHub runs a schedule only from the default branch, and disables it after 60 days without a commit to the repository; re-enable it under Actions. |

## 5. A personal-data breach

Lex records nothing about its readers and holds no account, so a breach would be of the infrastructure (the signing
key, the subscription) or of personal data inside published acts (R92). Under GDPR articles 33 and 34: assess what was
exposed and to whom; contain it (rotate the key, revoke access, roll back); notify the CNPD within 72 hours unless the
breach is unlikely to put anyone at risk; inform the people affected when the risk to them is high; and record every
breach, notified or not, with what was done. Wrong text from Lex's own derivation gets a public correction note and a new
release, and `verify` never reports a match for a text Lex later replaced.

## 6. The monthly cost ceiling

R114: one monthly ceiling over everything in `rg-lex-v3` (hosting, registry, retained releases, custody copies, the DNS
zone, the derive jobs), with alerts at 50, 80 and 100 per cent. The amount is the plan's N1 ceiling, 150 EUR a month
(about 120 expected once public); the owner may set another. The alerts go to the subscription's Owner role, by role and
through an action group whose receiver is that role, never to an e-mail address written here. The classic
`az consumption budget create` takes no notifications, so the command is its resource-group form, `create-with-rg`. It
changes money settings, so the owner runs it, signed in as the subscription's owner:

```powershell
$rg = 'rg-lex-v3'
az provider register --namespace Microsoft.Insights --wait
$ownerRole = '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'   # Azure's built-in Owner role
$group = az monitor action-group create -g $rg -n ag-lex-v3-owner --short-name lexowner `
  --action armrole subscription-owner $ownerRole usecommonalertschema --query id -o tsv
$notifications = [ordered]@{}
foreach ($percent in 50, 80, 100) {
  $notifications["actual-$percent"] = [ordered]@{
    enabled = $true; operator = 'GreaterThanOrEqualTo'; threshold = $percent
    'contact-emails' = @(); 'contact-roles' = @('Owner'); 'contact-groups' = @($group)
  }
}
$file = Join-Path ([IO.Path]::GetTempPath()) 'lex-v3-budget-notifications.json'
$notifications | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $file -Encoding utf8NoBOM
az consumption budget create-with-rg -g $rg -n lex-v3-monthly --category Cost --amount 150 --time-grain Monthly `
  --time-period start-date=2026-10-01 end-date=2027-10-31 --notifications "@$file"
Remove-Item -LiteralPath $file
# Read it back: the amount, this month's spend so far and the three alerts.
az consumption budget show-with-rg -g $rg -n lex-v3-monthly `
  --query "{amount:amount, spent:currentSpend.amount, unit:currentSpend.unit, alerts:keys(notifications)}"
```

The subscription keeps its earlier budget of 30 EUR a month, which the owner accepted on 2026-10-13. A budget only alerts;
it stops nothing. R114's order of cuts, should the ceiling be reached: the assistant first (it is off), then the warm
rollback revision (deactivate the previous revision), then non-essential exports. Law text, refusals, coverage and
retention are never cut.
