# Operating Lex after launch

How the public site at https://law.soufien.lu is run once it is open (the specification of record's R8, with R107 to
R117 for monitoring, incidents, the limit, backups, hosting, money and keys). What to do in an incident is
[INCIDENT-RUNBOOK.md](INCIDENT-RUNBOOK.md). The owner adopts this page with O17; until then it records how the site is
run today. It names no person: "the owner" and "the driver" are roles.

## 1. Who does what

- **The driver operates; the owner decides.** The driver (an AI agent working in this repository) builds, releases,
  deploys to a candidate, watches the monitor and keeps the status files. The owner decides, in person, the classes of the
  standing order of 2026-09-30: the production signing identity, production credentials, production deployment and
  promotion, money, and public legal claims, plus any new publisher scope and each publisher-traffic run the standing
  authorisations do not cover.
- **The review rule carries forward.** One slice a day, one pull request per slice, with its evidence (commands, counts,
  scope) in the description, one independent review of each pull request, one repair round, then a merge on green CI. The
  status files (`STATUS-WEB.md`, `STATUS-DATA.md`) change in the same pull request; `OWNER-BRIEF.md` is the owner's page.
- **Standing authorisations** (R87, O10, R108, O17; each in force once the owner gives it):
  - refresh runs under one bounded authorisation: a nightly feed check, a weekly full listing and a monthly byte
    re-check, each with a request budget, literal robots, the existing pacing and an automatic stop, never a request to
    eur-lex.europa.eu. Until O10 is given, each run waits for the owner's explicit yes;
  - data-only releases at most weekly (the same signed image, a new signed snapshot), each after every machine gate and a
    person's 20-triple hand-check against the publishers' files;
  - incident actions: the three of R108 (rollback, the maintenance page, blocking an abusive client).
- **What stays with the owner:** everything in the first point, any change to the legal pages' claims, any new money, and
  the rotation or revocation of a key.
- **The weekly checkpoint.** Thirty minutes: what was released, what the monitor saw, the month's spend against the
  ceiling, the data's age, the open owner items.

## 2. The platform

- **Hosting** (R113): Azure Container Apps, app `ca-lex-v3` in `rg-lex-v3`, France Central, environment `cae-lex-v3`. One
  server answers the pages, `/api/v3` and `/mcp` on one origin. One replica of 1 vCPU and 2 GiB per active revision; the
  indexes are inside the signed image. Multiple-revision mode: a release is a new revision, given traffic only by
  promotion.
- **No record of readers** (R109, R110): the server clears its logging providers and writes nothing about a request; the
  template configures no log store, no Application Insights and no session affinity. `PublicRequestRecordingTests` holds
  the server to it.
- **The per-client limit** (R109, R46; `src/Lex.V3.Api/V3ClientLimiter.cs`): 60 API requests (REST or MCP) a minute per
  client, at most 4 at once, then the typed refusal `rate_limited` with status 429 and `Retry-After`; pages the server
  renders, 300 a minute, each render holding one of the 4 places while it runs, and past either served as built; the
  site's other files are not counted. The client is the connection's
  address, or behind the platform's ingress the address it forwards, hashed at once with a key drawn at random for the
  process and replaced every day; only the hash and the counts are held, in memory, and each entry is forgotten about two
  minutes after the client's last request. A request from inside the host with no forwarded address (the platform's
  probes, the journeys, a release's own image run) is not counted. The privacy notice says so.
  - **What the tests do not prove.** The server's tests drive the handler with a made-up connection and header; none of
    them can see Container Apps' ingress, so none proves that the ingress forwards each reader's own address in
    `X-Forwarded-For`. That is the platform's documented behaviour and an assumption here. It is checked on each
    candidate before promotion, from two addresses (INCIDENT-RUNBOOK.md, section 3, "Before promoting"): if the
    ingress forwarded nothing, the limit would count no one; if it forwarded one address for everyone, the whole site
    would share one bucket.
- **Security headers** on every answer (`src/Lex.V3.Api/V3HostHeaders.cs`): HSTS for a year with subdomains,
  `Referrer-Policy: no-referrer`, `nosniff`, and a Content-Security-Policy: the page's own reviewed policy with
  `frame-ancestors 'none'`, or `default-src 'none'; frame-ancestors 'none'` for every answer that is no page.
- **The published key set** (R116): `https://law.soufien.lu/.well-known/lex-keys.json`, served from
  `src/Lex.V3.Api/well-known/lex-keys.json`, built into the server from the repository. It holds the release key,
  identity `lex-v3-release`, role `release`, valid from 2026-10-13 and to be replaced by 2027-10-13; its public key is
  `src/Lex.V3.Api/well-known/lex-v3-release.pem`. The private key stays in Azure Key Vault. A second signer's key joins
  the set when O16 gives it one. On a compromise: mark the key revoked with its date in the set, re-sign the retained
  releases with a new key, release, and post a notice.

## 3. The domain and the certificate

- `law.soufien.lu` is a CNAME to the app's default host, with the `asuid.law` TXT record holding the app's
  custom-domain verification id; both are in the `soufien.lu` zone. Since 2026-10-18 that zone is an Azure DNS zone in
  `rg-lex-v3` (the owner pointed the registrar at it), so the name goes down with the subscription: R117's risk, accepted
  by the owner while the subscription stays funded. Moving it to the registrar needs only those two records.
- The certificate is a free managed certificate (`deploy/bind-domain.ps1` bound it); Azure renews it while the CNAME
  stands. The monitor fails within 21 days of its end. Every deployment reads the bound domain back and keeps it.
- The site sends HSTS for a year: a fallback served from elsewhere must have a valid certificate for the name, or browsers
  refuse it.

## 4. Releases and data refreshes

A data refresh and a release are four steps, each with its own evidence:

1. **Acquisition** (publisher traffic): a run of `Lex.V3.Tool` against Legilux and the Publications Office, from this
   PC, under literal robots, request budgets and an automatic stop, never a request to eur-lex.europa.eu. It needs the
   owner's yes for each run until O10's standing authorisation exists. It writes custody (raw bytes before decoding,
   observations, journals); STATUS-DATA.md records each run.
2. **Derivation, on Azure** (R112): an Azure Container Apps job on a Flex workload profile (16 vCPU and 64 GiB; a
   derive peaks near 34 GiB) in a separate build environment in `rg-lex-v3`, made for the run and deleted after it, since an
   idle Flex profile is billed by the hour. The job reads custody through a read-only, container-scoped SAS and writes
   through a create-and-write one, both job secrets mounted as files and expiring within a day; it runs
   `Lex.V3.Tool rederive-inputs`, `derive` twice (A and B), compares every file, `rederive-seal`, runs the machine gates
   and uploads the mount with its manifest. The kit that launches it (its scripts and the installer that brings a mount
   back to the PC, verified file by file against the manifest) is the operator's, outside this public repository, because
   it handles storage keys. A derive takes about three hours, all on Linux (the PDF layout depends on the host's fonts,
   so the population is derived and released on Linux only).
3. **The release, on this PC**: from a clean checkout of the merged head, in `web/`, with the signing key piped from Key
   Vault to standard input and never written:

   ```powershell
   $vault = az keyvault list -g rg-lex-v3 --query "[0].name" -o tsv
   az keyvault secret show --vault-name $vault --name release-signing-key --query value -o tsv |
     node --expose-gc scripts/image-rehearsal.mjs --mount <the derived mount> --no-browser --keep `
       --signing-key - --signer lex-v3-release 1> rehearsal-report.json 2> rehearsal.log
   ```

   It builds the image, verifies and signs it, builds it again from scratch to the same digest, runs every probe that
   needs no browser against the image (the API journeys, the served pages, V2's absence, privacy: nothing written, no
   cookie, no other origin), publishes the release's assets and reads them back. No browser runs on this PC.
4. **Deployment and promotion**: `deploy/deploy.ps1 ... -Public -Apply` (INCIDENT-RUNBOOK.md, section 3), then the
   owner's promotion, then the previous revision deactivated so one replica is billed.

**The deploy kit's rules** (`deploy/deploy.ps1`, held by `web/test/deploy-kit.test.mjs` and `deploy-run.test.mjs`):
- it never signs in, never prints a secret and never promotes; without `-Apply` it only plans;
- the release is read back under the signing key and must be signed by `lex-v3-release`, never a rehearsal;
- the image is copied by its manifest digest and deployed by that digest, never a tag;
- the candidate gets no traffic, is probed at its own address, and is deactivated if the probe fails or it never becomes
  ready; a data age past the maximum stops promotion (exit 3);
- a bound domain is read back and kept, and a failed read stops the deployment;
- since the opening every deployment says `-Public`, which stops if any address rule exists;
- since #992, each deployment makes a revision of its own, `ca-lex-v3--<the release's suffix>-<8 hex digits of a SHA-256
  over its settings>` (the image, `-SearchEngines`, `-Public`, the address ranges and `deploy/main.bicep`); a revision that
  already exists is refused before anything changes, and is activated and probed by hand instead (INCIDENT-RUNBOOK.md,
  section 3); a redeployment must name the revision serving now with `-LiveRevision`, one of the app's; and the kit
  deactivates only a revision it made;
- since #993, the probe is handed `-SearchEngines` as `--search-engines` and holds robots.txt and the sitemap to it;
- promotion, rollback and the deactivation of the previous revision are printed for the owner, never run; the runbook's
  blocks for them stop at a failed step, wait until the target is running and healthy, and deactivate only once the
  traffic reads 100 per cent on the target (`web/test/runbook-blocks.test.mjs`).

## 5. Freshness and the 30-day limit

Every page and every API answer names, for each publisher, the newest completed collection it holds and its age (R87). The
maximum data age is 30 days (O11, the owner's choice of 2026-10-19):
- past it, every page shows the staleness banner and the monitor's `api` check fails;
- promotion refuses a release whose data is past it.

As served on 2026-10-27, Luxembourg's newest completed collection is of 2026-10-12, so it passes 30 days on 2026-11-11,
and the EU's is of 2026-10-05, so it passes on 2026-11-04. A refresh needs publisher traffic, so it waits for the
owner's yes until O10 exists. Data-only releases come at most weekly.

## 6. Monitoring

`.github/workflows/v3-monitor.yml` runs `web/scripts/site-monitor.mjs` every five minutes on GitHub's runners, outside
Azure. It asks five things any reader may ask: readiness, the home page with its security headers, one dated reading
page, the `coverage` operation with its freshness, and the certificate. A check that fails is asked again a minute later;
a second failure fails the run (GitHub reports a failed scheduled run to whoever last changed its schedule) and opens or
adds to one issue, closed again once the site answers. It needs no secret and records nothing of anyone's.

GitHub runs a schedule only from the repository's default branch, which is still the legacy `main`: the workflow acts
once its file is on that branch, or once `v3/integration` becomes the default. It always runs the monitor as
`v3/integration` holds it.

Not yet watched from outside: latency budgets, error rates and restarts (R107); the spend is watched by the budget.

## 7. Money

- **Measured costs:** one active replica of 1 vCPU and 2 GiB is about 23 to 26 EUR a month; a second active revision
  during a release doubles that while it lasts. An idle Flex profile cost about 0.09 EUR an hour, and a premium file share
  left behind about 10 EUR a week, until both were deleted: a build environment is deleted after each derive.
- **The ceiling** (R114): 150 EUR a month over `rg-lex-v3`, the plan's N1 amount, with alerts at 50, 80 and 100 per cent
  to the subscription's Owner role. The command is in INCIDENT-RUNBOOK.md, section 6; the owner runs it. Read the month's
  spend with
  `az consumption budget show-with-rg -g rg-lex-v3 -n lex-v3-monthly --query "{amount:amount, spent:currentSpend.amount}"`.
- **The order of cuts** at the ceiling: the assistant (off), then the warm rollback revision, then non-essential exports;
  never law text, refusals, coverage or retention.
- The trial credit ends on 2026-11-12; from then the payment method on file pays. A subscription that stops paying takes
  the site and its DNS zone down with it.

## 8. What is not done yet

- Copies of custody and of every signed release off this PC, checked by digest, with a restore drill (R111, O15).
- DNS, the key set and a fallback page outside the subscription (R117, O14), and a maintenance revision that can be
  switched on at night (the plan's item 10.14).
- A second signer with its own key (R116, O16).
- Latency, error-rate and restart alerts (R107), and the load test that re-sizes the limit (R109).
