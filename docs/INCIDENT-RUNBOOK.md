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

**Roll back to the previous revision** (S1). A deactivated revision is kept and can be activated again; it needs about a
minute to mount its index before it is ready.

```powershell
az containerapp revision activate -g rg-lex-v3 -n ca-lex-v3 --revision <previous revision>
az containerapp revision show -g rg-lex-v3 -n ca-lex-v3 --revision <previous revision> `
  --query "{health:properties.healthState, running:properties.runningState}"      # until Healthy and Running
az containerapp ingress traffic set -g rg-lex-v3 -n ca-lex-v3 --revision-weight <previous revision>=100
node web/scripts/site-monitor.mjs --second-look-ms 0
# Then, so one replica is billed, deactivate the one that failed (it is kept):
az containerapp revision deactivate -g rg-lex-v3 -n ca-lex-v3 --revision <failed revision>
```

**Forward again** to a revision that was rolled back from: the same three commands with that revision's name.

**Bring back a live revision that was deactivated by mistake** (the incident of 2026-10-27, section 4):

```powershell
az containerapp revision activate -g rg-lex-v3 -n ca-lex-v3 --revision <live revision>
az containerapp ingress traffic show -g rg-lex-v3 -n ca-lex-v3 -o table           # still 100 per cent to it?
curl.exe -s -o NUL -w "%{http_code}`n" https://law.soufien.lu/health/ready          # 204 once it holds the law
```

**Redeploy a release, or deploy a new one** (an owner's decision unless it is a rollback). Every deployment is
`deploy/deploy.ps1`: it verifies the release under the signing key, deploys it as a zero-traffic candidate, probes the
candidate at its own address and deactivates it if the probe fails; promotion is printed, never run. Since the opening,
every deployment says `-Public`.

```powershell
$subscription = az account show --query id -o tsv
$environment = az containerapp env show -g rg-lex-v3 -n cae-lex-v3 --query id -o tsv
$registry = az acr list -g rg-lex-v3 --query "[0].loginServer" -o tsv
$identity = az identity list -g rg-lex-v3 --query "[0].id" -o tsv
$live = az containerapp revision list -g rg-lex-v3 -n ca-lex-v3 --query "[?properties.trafficWeight==``100``].name | [0]" -o tsv
pwsh -File deploy/deploy.ps1 -Subscription $subscription -ResourceGroup rg-lex-v3 -AppName ca-lex-v3 `
  -EnvironmentId $environment -Registry $registry -IdentityResourceId $identity `
  -SigningPublicKey src/Lex.V3.Api/well-known/lex-v3-release.pem -SignerIdentity lex-v3-release `
  -Release <release directory> -LiveRevision $live -Public -SearchEngines indexed -Apply
# Promotion, once the probe passed (the owner's):
az containerapp ingress traffic set -g rg-lex-v3 -n ca-lex-v3 --revision-weight <candidate>=100
az containerapp revision deactivate -g rg-lex-v3 -n ca-lex-v3 --revision $live
```

Until the fix on `writer/deploy-live-safe` is merged, never run `deploy.ps1` with the release the live revision already
serves (section 4): to change only a setting of the live release, wait for that fix or deploy a newer release.

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
| **2026-10-27, about 01:10 to 01:13 UTC: a redeploy deactivated the live revision.** The release already live was redeployed to change only `LEX_V3_SEARCH_ENGINES`. `deploy.ps1` names the revision after the release, so Azure refused it ("revision with suffix ... already exists"), and the kit's failure handler then deactivated the revision of that name, the live one. The site answered nothing for about three minutes. | The site down right after a deployment that failed at Azure Resource Manager. | Reactivate the live revision (section 3), check readiness. The fix (a unique suffix per deployment, a collision refused before anything is written, and never deactivating a revision the deployment did not create) is on `writer/deploy-live-safe`. |
| A candidate never becomes ready | The deployment fails at Azure's progress deadline; `/health/ready` on the candidate answers 503 (no law mounted). | The kit deactivates it; no traffic moved. Read its replica's console for the mount's refusal. |
| The probe fails | `deploy.ps1` stops with "failed its probe". | The kit deactivated the candidate; the live revision is untouched. |
| Promotion refused for the data's age | The probe exits 3: a publisher's newest completed collection is older than 30 days. | No promotion command is printed; a release with newer data is the way forward (OPERATING.md, "Freshness"). |
| Data past 30 days on the live site | Every page shows the staleness banner; the monitor's `api` check fails ("stale"). | S2: a refresh run (needs the owner's per-run yes), a derive and a release. |
| The certificate near its end | The monitor's `certificate` check fails within 21 days of the end. | The managed certificate renews itself while `law` stays a CNAME to the app; check the binding with `az containerapp hostname list -g rg-lex-v3 -n ca-lex-v3 -o table`, or rebind with `deploy/bind-domain.ps1`. |
| The limit sees no client | After a platform change the ingress might stop forwarding the client's address; the limit would then count no one. | Check from one machine: `1..120 \| % { (Invoke-WebRequest -Method Post -Uri https://law.soufien.lu/api/v3/coverage -ContentType application/json -Body '{"operation_id":"coverage","parameters":{}}' -SkipHttpErrorCheck).StatusCode } \| Group-Object -NoElement` must show some 429 (it spends that machine's minute). |
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
