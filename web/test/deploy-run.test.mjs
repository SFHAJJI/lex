// `deploy/deploy.ps1` run whole under pwsh, as the owner runs it, with the Azure CLI, oras and node made up as PowerShell
// functions (a function comes before any command on the path, so nothing reaches Azure, a registry or a probe). The made-up
// node records the exact argument list the script hands the probe: PowerShell unrolls a one-element array returned by an
// `if`, so `$openingArgs = if ($Public) { @('--opening') } else { @() }` was the string '--opening', and `@openingArgs` handed
// it over one character at a time (`- - o p e n i n g`); the probe never read --opening, and a -Public candidate whose legal
// pages showed what the opening refuses (then a placeholder; since the owner's decision of 2026-10-19 that the site names no
// person and gives no contact, an e-mail address, a mail or telephone link, a placeholder or a postcode) passed and was
// offered for promotion (the review of #989, F1). And a -Public deployment on
// the still-private app is refused before anything in Azure changes (F2): it would deploy with openToPublic, which removes
// the address rules before the candidate is probed, and a failed probe deactivates only the candidate.
//
// The incident of 2026-10-27 (STATUS-WEB.md): the live release, redeployed to change only LEX_V3_SEARCH_ENGINES, was given
// the live revision's own suffix, so Azure refused the deployment ("revision with suffix ... already exists"), and the
// failure handler then deactivated the revision of that name: the live one. The made-up Azure CLI holds the app's revisions
// as Azure does: a deployment whose suffix names one that exists fails and creates nothing; one that goes through adds its
// revision.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { after } from "node:test";
import { fileURLToPath } from "node:url";

import { legalPageTextFailures } from "../scripts/legal-pages.mjs";

const DEPLOY_SCRIPT = fileURLToPath(new URL("../../deploy/deploy.ps1", import.meta.url));
const quote = (text) => `'${text.replaceAll("'", "''")}'`;

// A release as the script reads it once the release is read back (the read-back is the made-up node's): its manifest, and a
// key file that is only resolved.
function releaseOf(version, digest) {
  const directory = mkdtempSync(join(tmpdir(), "lex-deploy-run-"));
  after(() => rmSync(directory, { recursive: true, force: true }));
  writeFileSync(join(directory, "release-manifest.json"), JSON.stringify({
    version, image: { manifest_digest: `sha256:${digest}` }, corpus: { sha256: "c".repeat(64) },
  }));
  return directory;
}
const releaseDirectory = releaseOf("v3-20261019T060000Z-0123456789ab", "a".repeat(64));
const publicKey = join(releaseDirectory, "signing.pem");
writeFileSync(publicKey, "made up: the release read-back is the made-up node's\n");
// The release the incident redeployed, by its real name and image, and the revision it served as at 100 per cent.
const INCIDENT_RELEASE = releaseOf("v3-20261026T180319Z-8958ec5fa63f", "93cdd51ef83f5c8f96478014e5ce89eb734c7d97c59172f4d294c348052f96b2");
const INCIDENT_LIVE = "ca-lex-v3--v3-20261026t180319z-8958ec5fa63f";

const LIVE = "ca-lex-v3--v3-live";
const PRIVATE_RULES = '[{"name":"private-copy-1","ipAddressRange":"203.0.113.7/32","action":"Allow"}]';

// A legal page of the candidate's that gives an e-mail address in a mail link, and what the real check says of it
// (`legal-pages.mjs`): the owner's decision of 2026-10-19 keeps both off the site.
const CONTACT_PAGE = '<!doctype html><p>Contact: <a href="mailto:someone@example.invalid">someone@example.invalid</a>.</p>';
const [SHOWS_CONTACT] = legalPageTextFailures("/fr/legal-notice.html", CONTACT_PAGE);

// The made-up tools. `RULES` is what the app's ipSecurityRestrictions read answers ("[]" or "null" once the site is open, the
// private copy's rule before), `RULES_READ_FAILS` makes that read fail; `LEGAL_PAGE_FAILURE` is what the real check says of a
// candidate's legal page, which the made-up probe says as the real one does (`legalPageFailures`): with the token --opening,
// a failure, and the probe exits 1; without it, a note beside a pass. `REVISIONS` names the app's revisions, comma-separated
// (the live one by default), and `REVISIONS_READ_FAILS` makes their listing fail; a deployment whose revision exists fails as
// Azure's did on 2026-10-27, creating nothing, and `DEPLOYMENT_FAILS` fails one after its revision is created (a candidate
// that never becomes ready); `CANDIDATE_FAILS` makes a revision's state read say it failed to provision.
const STUBS = `
  $global:calls = [System.Collections.Generic.List[string]]::new()
  $global:revisions = [System.Collections.Generic.List[string]]::new()
  foreach ($name in "$env:REVISIONS" -split ',') { if ($name) { $global:revisions.Add($name) } }
  function global:az {
    $line = $args -join ' '
    $global:LASTEXITCODE = 0
    if ($line -like 'deployment group create*') {
      $file = ($args | Where-Object { "$_".StartsWith('@') } | Select-Object -First 1).Substring(1)
      $parameters = (Get-Content -Raw $file | ConvertFrom-Json).parameters
      $global:calls.Add("az deployment group create openToPublic=$($parameters.openToPublic.value) revisionSuffix=$($parameters.revisionSuffix.value) liveRevision=$($parameters.liveRevision.value) searchEngines=$($parameters.searchEngines.value)")
      $revision = "$($parameters.appName.value)--$($parameters.revisionSuffix.value)"
      if ($global:revisions -contains $revision) {
        Write-Host "ERROR: made up: revision with suffix $($parameters.revisionSuffix.value) already exists"
        $global:LASTEXITCODE = 1
        return
      }
      $global:revisions.Add($revision)
      if ($env:DEPLOYMENT_FAILS) { $global:LASTEXITCODE = 1 }
      return
    }
    $global:calls.Add("az $line")
    $named = if ($args -contains '--revision') { $args[[array]::IndexOf($args, '--revision') + 1] } else { '' }
    if ($line -like 'containerapp show*ipSecurityRestrictions*') {
      if ($env:RULES_READ_FAILS) { $global:LASTEXITCODE = 3; return }
      return $env:RULES
    }
    if ($line -like 'containerapp show*customDomains*') { return '[]' }
    if ($line -like 'containerapp revision list*') {
      if ($env:REVISIONS_READ_FAILS) { $global:LASTEXITCODE = 3; return }
      return @($global:revisions)
    }
    if ($line -like 'containerapp revision show*runningState*') {
      $provisioning = if ($env:CANDIDATE_FAILS) { 'Failed' } else { 'Provisioned' }
      return '{"running":"Running","health":"Healthy","provisioning":"' + $provisioning + '","active":true,"fqdn":"' + $named + '.example.azurecontainerapps.io"}'
    }
    if ($line -like 'containerapp revision show*--query properties.active *') {
      if ($global:revisions -contains $named) { return 'true' }
      $global:LASTEXITCODE = 3
      return
    }
    if ($line -like 'acr login*') { return 'a made-up token' }
  }
  function global:oras { $global:calls.Add("oras $($args -join ' ')"); $global:LASTEXITCODE = 0 }
  function global:node {
    # As the real node would receive them: a native command is handed no $null argument (a function is), and a string
    # splatted to either is handed over one character at a time.
    $argv = @($args | Where-Object { $null -ne $_ })
    $global:calls.Add('node ' + (ConvertTo-Json -Compress -InputObject $argv))
    $global:LASTEXITCODE = 0
    if ($env:LEGAL_PAGE_FAILURE -and ($argv -ccontains '--origin')) {
      if ($argv -ccontains '--opening') {
        Write-Host "- the legal pages are not ready to publish, so the site may not open: $env:LEGAL_PAGE_FAILURE"
        $global:LASTEXITCODE = 1
      }
      else { Write-Host "the legal pages are not ready to publish, so the site may not open (deploy.ps1 -Open and -Public refuse it): $env:LEGAL_PAGE_FAILURE" }
    }
  }`;

/**
 * Runs deploy.ps1 with these parameters under the made-up tools: what it said, whether it threw, and every call it made; the
 * suffix it planned (its step "deploy the candidate revision"), each suffix it handed the template, and each revision it
 * deactivated. `live` is -LiveRevision (none when empty), `release` the release directory.
 */
function deploy(parameters, env = {}, { live = LIVE, release = releaseDirectory } = {}) {
  const common = ["-Subscription", "s", "-ResourceGroup", "rg", "-EnvironmentId", "env", "-Registry", "crlex.azurecr.io", "-IdentityResourceId", "id",
    "-SigningPublicKey", quote(publicKey), "-Release", quote(release), ...(live ? ["-LiveRevision", live] : [])];
  const script = `${STUBS}
    try { & ${quote(DEPLOY_SCRIPT)} ${[...common, ...parameters].join(" ")}; 'RESULT:returned' } catch { "RESULT:threw $($_.Exception.Message)" }
    foreach ($call in $global:calls) { "CALL:$call" }`;
  // A wait that never ends (the script's own is 20 minutes) fails here instead.
  const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], {
    encoding: "utf8", timeout: 120_000,
    env: { ...process.env, RULES: "", RULES_READ_FAILS: "", LEGAL_PAGE_FAILURE: "", REVISIONS: LIVE, REVISIONS_READ_FAILS: "", DEPLOYMENT_FAILS: "", CANDIDATE_FAILS: "", ...env },
  });
  assert.equal(run.error, undefined, `pwsh could not run: ${run.error}`);
  const lines = run.stdout.split(/\r?\n/);
  const result = lines.find((line) => line.startsWith("RESULT:")) ?? `no result: ${run.stdout}${run.stderr}`;
  const calls = lines.filter((line) => line.startsWith("CALL:")).map((line) => line.slice("CALL:".length));
  const probes = calls.filter((call) => call.startsWith("node ")).map((call) => JSON.parse(call.slice("node ".length)))
    .filter((argv) => argv.includes("--origin"));
  const deployments = calls.filter((call) => call.startsWith("az deployment group create "))
    .map((call) => Object.fromEntries(call.slice("az deployment group create ".length).split(" ").map((pair) => pair.split("="))));
  const deactivated = calls.filter((call) => call.startsWith("az containerapp revision deactivate "))
    .map((call) => call.slice(call.indexOf("--revision ") + "--revision ".length));
  const planned = /^== deploy the candidate revision (\S+)$/m.exec(run.stdout)?.[1];
  return { out: run.stdout, result, calls, probes, deployments, deactivated, planned, candidate: deployments[0] && `ca-lex-v3--${deployments[0].revisionSuffix}` };
}

/** Every call that changes something in Azure or the registry: all but the release read-back, the subscription and the reads. */
const writes = (run) => run.calls.filter((call) => !/^node |^az account set |^az containerapp show |^az containerapp revision (list|show) /.test(call));
const promotes = (run, revision) => run.out.includes(`--revision-weight ${revision}=100`);

/** The probe's arguments from the signer on: what -Public, -BrowserProbe and the promotion check add, token by token. */
const probeTail = (argv) => argv.slice(argv.indexOf("--signer"));
const indexOf = (calls, pattern) => calls.findIndex((call) => pattern.test(call));

test("a -Public deployment's probe reads the token --opening, just before --promotion-check (F1)", () => {
  const run = deploy(["-SignerIdentity", "signer", "-Public", "-Apply"], { RULES: "[]" });
  assert.equal(run.result, "RESULT:returned", run.out);
  assert.equal(run.probes.length, 1, run.calls.join("\n"));
  const [probe] = run.probes;
  assert.deepEqual(probeTail(probe), ["--signer", "signer", "--opening", "--promotion-check"], JSON.stringify(probe));
  assert.ok(probe.every((token) => token.length > 1), `no argument handed over one character at a time: ${JSON.stringify(probe)}`);
  assert.equal(probe[0], "--import", "without -BrowserProbe the probe runs under the browser trap");
  assert.match(probe[1], /^file:\/\/\/\S+browser-trap\.mjs$/);
  assert.deepEqual(probe.slice(probe.indexOf("--origin"), probe.indexOf("--origin") + 2), ["--origin", `https://${run.candidate}.example.azurecontainerapps.io`]);
  assert.deepEqual(run.deployments.map((deployment) => deployment.openToPublic), ["True"], run.calls.join("\n"));
  assert.ok(promotes(run, run.candidate), "a candidate that passes is offered for promotion");
});

test("a -Public candidate whose legal page shows an e-mail address fails its probe, is deactivated, and is offered for no promotion (F1)", () => {
  assert.match(SHOWS_CONTACT, /^\/fr\/legal-notice\.html shows an e-mail address, a mail or telephone link, which the owner's decision of 2026-10-19 keeps off the site/);
  const run = deploy(["-SignerIdentity", "signer", "-Public", "-Apply"], { RULES: "[]", LEGAL_PAGE_FAILURE: SHOWS_CONTACT });
  assert.equal(run.result, "RESULT:threw The candidate failed its probe and was deactivated; no traffic moved.", run.out);
  assert.ok(run.out.includes(`- the legal pages are not ready to publish, so the site may not open: ${SHOWS_CONTACT}`), run.out);
  assert.ok(run.calls.includes(`az containerapp revision deactivate -g rg -n ca-lex-v3 --revision ${run.candidate}`), run.calls.join("\n"));
  assert.deepEqual(run.deactivated, [run.candidate], "the candidate alone");
  assert.doesNotMatch(run.out, /--revision-weight/, "no promotion command is printed");
  // The private copy's candidate with the same page: its probe reads no --opening, so it says the page beside its pass, and
  // the candidate is kept and offered for promotion on the private copy.
  const privately = deploy(["-SignerIdentity", "signer", "-AllowedCidr", "203.0.113.7/32", "-Apply"], { RULES: PRIVATE_RULES, LEGAL_PAGE_FAILURE: SHOWS_CONTACT });
  assert.equal(privately.result, "RESULT:returned", privately.out);
  assert.ok(privately.out.includes(`(deploy.ps1 -Open and -Public refuse it): ${SHOWS_CONTACT}`), privately.out);
  assert.ok(!privately.calls.some((call) => call.startsWith("az containerapp revision deactivate")), privately.calls.join("\n"));
  assert.ok(promotes(privately, privately.candidate), privately.out);
});

test("the browser probes, asked for, are handed over whole beside --opening, and without the trap (F1)", () => {
  // Nothing starts a browser here: node is the made-up function, which only records its arguments.
  const run = deploy(["-SignerIdentity", "signer", "-Public", "-BrowserProbe", "-Apply"], { RULES: "[]" });
  assert.equal(run.result, "RESULT:returned", run.out);
  const [probe] = run.probes;
  assert.deepEqual(probeTail(probe), ["--signer", "signer", "--browser", "--opening", "--promotion-check"], JSON.stringify(probe));
  assert.ok(!probe.includes("--import"), "the browser probes run without the trap");
});

test("the private copy's probe reads no --opening, and its deployment asks nothing about the rules first (F1, F2)", () => {
  const run = deploy(["-SignerIdentity", "signer", "-AllowedCidr", "203.0.113.7/32", "-Apply"], { RULES: PRIVATE_RULES });
  assert.equal(run.result, "RESULT:returned", run.out);
  const [probe] = run.probes;
  assert.deepEqual(probeTail(probe), ["--signer", "signer", "--promotion-check"], JSON.stringify(probe));
  assert.ok(!probe.some((token) => token.includes("opening") || token === "o"), JSON.stringify(probe));
  assert.deepEqual(run.deployments.map((deployment) => deployment.openToPublic), ["False"], run.calls.join("\n"));
  // The rules are read once the deployment is made (the read-back of who ingress admits), not before it.
  assert.ok(indexOf(run.calls, /ipSecurityRestrictions/) > indexOf(run.calls, /^az deployment group create/), run.calls.join("\n"));
});

test("a -Public deployment on the private app is refused before anything in Azure changes: open it with -Open first (F2)", () => {
  for (const [what, env, said] of [
    ["the private copy's rule", { RULES: PRIVATE_RULES }, /^RESULT:threw The site is private: open it with deploy\.ps1 -Open first \(1 address rule\(s\): private-copy-1 203\.0\.113\.7\/32\)\. Nothing was deployed/],
    ["two rules, one naming no range", { RULES: `[${PRIVATE_RULES.slice(1, -1)},{"name":"other","action":"Deny"}]` },
      /^RESULT:threw The site is private: open it with deploy\.ps1 -Open first \(2 address rule\(s\): private-copy-1 203\.0\.113\.7\/32, other\)\. Nothing was deployed/],
    ["a read that fails", { RULES_READ_FAILS: "1" }, /^RESULT:threw Whether ca-lex-v3 is open could not be read; nothing was deployed/],
    ["a read that is not JSON", { RULES: "not json" }, /^RESULT:threw Whether ca-lex-v3 is open could not be read; nothing was deployed/],
  ]) {
    const run = deploy(["-SignerIdentity", "signer", "-Public", "-Apply"], env);
    assert.match(run.result, said, `${what}: ${run.out}`);
    // Only the release read back (node, no probe), the subscription chosen, and the rules read: no image copied, no
    // template deployed or previewed, no revision touched, no rule changed.
    const changes = run.calls.filter((call) => !/^node |^az account set |^az containerapp show .*ipSecurityRestrictions/.test(call));
    assert.deepEqual(changes, [], `${what}: ${run.calls.join("\n")}`);
    assert.equal(run.probes.length, 0, what);
    assert.ok(run.calls.some((call) => /^az containerapp show -g rg -n ca-lex-v3 --query properties\.configuration\.ingress\.ipSecurityRestrictions -o json$/.test(call)), what);
  }
});

test("on the open site the rules are read before the image is copied or the template deployed, then the deployment goes on (F2)", () => {
  for (const rules of ["[]", "null", ""]) {
    const run = deploy(["-SignerIdentity", "signer", "-Public", "-Apply"], { RULES: rules });
    assert.equal(run.result, "RESULT:returned", `${JSON.stringify(rules)}: ${run.out}`);
    const read = indexOf(run.calls, /ipSecurityRestrictions/);
    assert.ok(read >= 0 && read < indexOf(run.calls, /^oras /) && read < indexOf(run.calls, /^az deployment group /), run.calls.join("\n"));
  }
});

test("a -Public plan run says it would check that the site is open, and asks Azure nothing (F2)", () => {
  const run = deploy(["-Public"], { RULES: PRIVATE_RULES });
  assert.equal(run.result, "RESULT:returned", run.out);
  assert.deepEqual(run.calls.filter((call) => !call.startsWith("node ")), [], "a plan run runs no Azure or registry command");
  assert.match(run.out, /> az containerapp show -g rg -n ca-lex-v3 --query properties\.configuration\.ingress\.ipSecurityRestrictions -o json: any address rule stops a -Public deployment here/);
  assert.match(run.out, /--opening --promotion-check/, "the planned probe names --opening");
  // The private copy's plan has no such check.
  const privately = deploy(["-AllowedCidr", "203.0.113.7/32"], { RULES: PRIVATE_RULES });
  assert.equal(privately.result, "RESULT:returned", privately.out);
  assert.doesNotMatch(privately.out, /any address rule stops a -Public deployment/);
});

test("the incident of 2026-10-27: the live release redeployed with -SearchEngines indexed is a new revision, deployed and probed, and nothing is deactivated", () => {
  // The driver's command of 01:10 UTC, as a run: the release live at 100 per cent, again, to change one setting.
  const run = deploy(["-SignerIdentity", "signer", "-Public", "-SearchEngines", "indexed", "-Apply"],
    { RULES: "[]", REVISIONS: `ca-lex-v3--v3-20261018t000000z-000000000000,${INCIDENT_LIVE}` }, { release: INCIDENT_RELEASE, live: INCIDENT_LIVE });
  assert.equal(run.result, "RESULT:returned", run.out);
  assert.deepEqual(run.deactivated, [], `nothing is deactivated: ${run.calls.join("\n")}`);
  assert.equal(run.deployments.length, 1, run.calls.join("\n"));
  const [deployment] = run.deployments;
  assert.equal(deployment.liveRevision, INCIDENT_LIVE, "the live revision keeps all the traffic");
  assert.equal(deployment.searchEngines, "indexed");
  assert.notEqual(run.candidate, INCIDENT_LIVE, "a revision of its own, not the live one's name");
  assert.match(deployment.revisionSuffix, /^v3-20261026t180319z-8958ec5fa63f-[0-9a-f]{8}$/, "the suffix still names the release");
  assert.equal(run.planned, deployment.revisionSuffix, "the step says the suffix it deploys");
  const [probe] = run.probes;
  assert.equal(probe[probe.indexOf("--origin") + 1], `https://${run.candidate}.example.azurecontainerapps.io`, "the new revision is probed at its own address");
  assert.ok(promotes(run, run.candidate), run.out);
  // The revisions are read before anything in Azure changes: before the image is copied and the template deployed.
  const read = indexOf(run.calls, /^az containerapp revision list -g rg -n ca-lex-v3 --all /);
  assert.ok(read >= 0 && read < indexOf(run.calls, /^az acr login |^oras /) && read < indexOf(run.calls, /^az deployment group /), run.calls.join("\n"));
});

test("the revision suffix is the release's and a digest of the deployment's settings: one per setting, the same each time, within Azure's rules", () => {
  const plan = (parameters, app = []) => deploy([...parameters, ...app], {}, { release: INCIDENT_RELEASE, live: INCIDENT_LIVE });
  const runs = [["-Public", "-SearchEngines", "indexed"], ["-Public"], ["-AllowedCidr", "203.0.113.7/32"], ["-AllowedCidr", "203.0.113.7/32,198.51.100.0/24"],
    ["-AllowedCidr", "203.0.113.7/32", "-SearchEngines", "indexed"]].map((parameters) => plan(parameters));
  const suffixes = runs.map((run) => run.planned);
  assert.equal(new Set(suffixes).size, suffixes.length, `each setting its own suffix: ${suffixes.join(" ")}`);
  assert.equal(plan(["-Public", "-SearchEngines", "indexed"]).planned, suffixes[0], "the same settings name the same suffix");
  for (const [index, suffix] of suffixes.entries()) {
    assert.equal(runs[index].result, "RESULT:returned", runs[index].out);
    assert.deepEqual(runs[index].calls.filter((call) => !call.startsWith("node ")), [], "a plan run asks Azure nothing");
    assert.match(suffix, /^v3-20261026t180319z-8958ec5fa63f-[0-9a-f]{8}$/);
    // Azure's rules (the Container Apps documentation, "Revisions"; az's own check): lower-case letters, digits and '-', a
    // letter first and a letter or digit last, no '--', at most 64 characters; and the revision's own address is
    // <revision>.<environment domain>, so the revision's whole name is one DNS label, at most 63 characters.
    assert.match(suffix, /^[a-z][a-z0-9-]*[a-z0-9]$/);
    assert.ok(!suffix.includes("--") && suffix.length <= 64, suffix);
    assert.ok(`ca-lex-v3--${suffix}`.length <= 63, `ca-lex-v3--${suffix}`);
    // The plan says the revision it would make, and that the digest is of the settings.
    assert.ok(runs[index].out.includes(`  revision ca-lex-v3--${suffix}: the release's suffix and the digest ${suffix.slice(-8)} of this deployment's settings`), runs[index].out);
  }
  // The longest app name Azure allows (32 characters): the release's part is shortened from the front, the digest kept.
  const long = plan(["-Public"], ["-AppName", "ca-lex-v3-the-longest-name-is-32"]);
  assert.equal(long.result, "RESULT:returned", long.out);
  assert.match(long.planned, /^[a-z][a-z0-9-]*-8958ec5fa63f-[0-9a-f]{8}$/);
  assert.equal(long.planned.slice(-8), suffixes[1].slice(-8), "the same settings, the same digest");
  assert.ok(`ca-lex-v3-the-longest-name-is-32--${long.planned}`.length <= 63);
  // A name that leaves no room for a suffix is refused before anything runs.
  const tooLong = plan(["-Public"], ["-AppName", "ca-lex-v3-a-name-far-longer-than-azure-allows-for-any-app-at-all"]);
  assert.match(tooLong.result, /^RESULT:threw No revision of ca-lex-v3-a-name-far-longer-than-azure-allows-for-any-app-at-all can be named for this release/, tooLong.out);
  assert.deepEqual(tooLong.calls.filter((call) => !call.startsWith("node ")), []);
});

test("a revision that already exists is refused before anything in Azure changes, and nothing is deactivated", () => {
  const settings = ["-SignerIdentity", "signer", "-AllowedCidr", "203.0.113.7/32"];
  const taken = `ca-lex-v3--${deploy(settings).planned}`;
  for (const [what, revisions, live] of [
    ["an earlier deployment of this release with these settings, now inactive", `${LIVE},${taken}`, LIVE],
    ["the live revision itself", taken, taken],
  ]) {
    const run = deploy([...settings, "-Apply"], { RULES: PRIVATE_RULES, REVISIONS: revisions }, { live });
    assert.match(run.result, new RegExp(`^RESULT:threw The revision ${taken} already exists in ca-lex-v3 .*Nothing was deployed and nothing changed`), `${what}: ${run.out}`);
    assert.deepEqual(writes(run), [], `${what}: no Azure write: ${run.calls.join("\n")}`);
    assert.deepEqual(run.deactivated, [], what);
    assert.equal(run.probes.length, 0, what);
  }
  // The revisions unread while a live revision serves: no revision could be told to be this deployment's, so none is made.
  const unread = deploy([...settings, "-Apply"], { RULES: PRIVATE_RULES, REVISIONS_READ_FAILS: "1" });
  assert.match(unread.result, /^RESULT:threw The revisions of ca-lex-v3 could not be read; nothing was deployed/, unread.out);
  assert.deepEqual(writes(unread), [], unread.calls.join("\n"));
  // A plan run naming the computed revision as -LiveRevision is refused too, and asks Azure nothing.
  const planned = deploy(settings, {}, { live: taken });
  assert.match(planned.result, new RegExp(`^RESULT:threw The revision ${taken} already exists in ca-lex-v3 `), planned.out);
  assert.deepEqual(planned.calls.filter((call) => !call.startsWith("node ")), []);
});

test("a redeployment names the revision serving now, and it must be one of the app's: otherwise nothing changes (the review's note 1)", () => {
  const settings = ["-SignerIdentity", "signer", "-AllowedCidr", "203.0.113.7/32", "-Apply"];
  // The app holds a revision and -LiveRevision is not given: the template would send all traffic to the unprobed candidate.
  const unnamed = deploy(settings, { RULES: PRIVATE_RULES, REVISIONS: LIVE }, { live: "" });
  assert.match(unnamed.result, /^RESULT:threw ca-lex-v3 holds 1 revision\(s\), so this is a redeployment: name the revision serving now with -LiveRevision\..*Nothing was deployed and nothing changed\./, unnamed.out);
  assert.deepEqual(writes(unnamed), [], unnamed.calls.join("\n"));
  assert.deepEqual(unnamed.deployments, []);
  assert.deepEqual(unnamed.deactivated, []);
  // -LiveRevision names a revision the app does not hold.
  const stranger = deploy(settings, { RULES: PRIVATE_RULES, REVISIONS: "ca-lex-v3--v3-20261001t000000z-000000000000" }, { live: LIVE });
  assert.match(stranger.result, new RegExp(`^RESULT:threw -LiveRevision ${LIVE} is not a revision of ca-lex-v3 .*Nothing was deployed and nothing changed\\.`), stranger.out);
  assert.deepEqual(writes(stranger), [], stranger.calls.join("\n"));
  assert.deepEqual(stranger.deployments, []);
});

test("a failed deployment deactivates only a revision it created: never the live one, and nothing when it cannot tell", () => {
  // The incident's shape: the name this deployment computes is the live revision's, and the deployment fails.
  const settings = ["-SignerIdentity", "signer", "-Public", "-SearchEngines", "indexed"];
  const live = `ca-lex-v3--${deploy(settings, {}, { release: INCIDENT_RELEASE, live: INCIDENT_LIVE }).planned}`;
  const incident = deploy([...settings, "-Apply"], { RULES: "[]", REVISIONS: live, DEPLOYMENT_FAILS: "1" }, { release: INCIDENT_RELEASE, live });
  assert.ok(!incident.deactivated.includes(live), `the live revision ${live} was deactivated: ${incident.calls.join("\n")}`);
  assert.deepEqual(incident.deactivated, [], incident.calls.join("\n"));
  assert.match(incident.result, /^RESULT:threw /, incident.out);
  assert.deepEqual(incident.deployments, [], "Azure is not asked to deploy a revision it holds");

  // A new candidate that Azure created and then failed (one that never becomes ready): deactivated, and only it.
  const failed = deploy([...settings, "-Apply"], { RULES: "[]", REVISIONS: INCIDENT_LIVE, DEPLOYMENT_FAILS: "1" }, { release: INCIDENT_RELEASE, live: INCIDENT_LIVE });
  assert.match(failed.result, /^RESULT:threw The candidate revision did not deploy .*It was deactivated if it was active, and no traffic moved to it\./, failed.out);
  assert.notEqual(failed.candidate, INCIDENT_LIVE);
  assert.deepEqual(failed.deactivated, [failed.candidate], failed.calls.join("\n"));

  // A first deployment whose revisions could not be read: whether the failed revision is its own is not known, so nothing is
  // deactivated, and it says so.
  const unknown = deploy([...settings, "-Apply"], { RULES: "[]", REVISIONS_READ_FAILS: "1", DEPLOYMENT_FAILS: "1" }, { release: INCIDENT_RELEASE, live: "" });
  assert.deepEqual(unknown.deactivated, [], unknown.calls.join("\n"));
  assert.match(unknown.result, /^RESULT:threw The candidate revision did not deploy .*Nothing was deactivated: whether ca-lex-v3--\S+ is this deployment's own is not known/, unknown.out);
  assert.match(unknown.out, /is not deactivated/, unknown.out);
});

test("a candidate that fails its probe or its provisioning is deactivated only when this deployment created it", () => {
  for (const [what, parameters, env] of [
    ["its probe fails", ["-Public"], { RULES: "[]", LEGAL_PAGE_FAILURE: SHOWS_CONTACT }],
    ["it fails to provision", ["-AllowedCidr", "203.0.113.7/32"], { RULES: PRIVATE_RULES, CANDIDATE_FAILS: "1" }],
  ]) {
    const run = deploy(["-SignerIdentity", "signer", ...parameters, "-Apply"], env);
    assert.match(run.result, /^RESULT:threw The candidate /, `${what}: ${run.out}`);
    assert.deepEqual(run.deactivated, [run.candidate], `${what}: the candidate alone: ${run.calls.join("\n")}`);
    const unknown = deploy(["-SignerIdentity", "signer", ...parameters, "-Apply"], { ...env, REVISIONS_READ_FAILS: "1" }, { live: "" });
    assert.match(unknown.result, /^RESULT:threw The candidate /, `${what}: ${unknown.out}`);
    assert.deepEqual(unknown.deactivated, [], `${what}: nothing is deactivated when the revisions could not be read: ${unknown.calls.join("\n")}`);
    assert.match(unknown.out, /is not deactivated/, `${what}: ${unknown.out}`);
  }
});

test("a new release over the live revision deploys as it did: one new candidate, the live one kept, probed at its own address and offered for promotion", () => {
  const run = deploy(["-SignerIdentity", "signer", "-AllowedCidr", "203.0.113.7/32", "-Apply"], { RULES: PRIVATE_RULES });
  assert.equal(run.result, "RESULT:returned", run.out);
  assert.equal(run.deployments.length, 1, run.calls.join("\n"));
  assert.equal(run.deployments[0].liveRevision, LIVE, "the live revision keeps all the traffic");
  assert.ok(run.candidate.startsWith("ca-lex-v3--v3-20261019t060000z-0123456789ab"), `the revision names the release: ${run.candidate}`);
  assert.notEqual(run.candidate, LIVE);
  assert.equal(run.planned, run.deployments[0].revisionSuffix, "the step says the suffix it deploys");
  assert.deepEqual(run.deactivated, [], run.calls.join("\n"));
  const [probe] = run.probes;
  assert.equal(probe[probe.indexOf("--origin") + 1], `https://${run.candidate}.example.azurecontainerapps.io`);
  assert.ok(promotes(run, run.candidate), run.out);
  assert.ok(run.out.includes(`After promotion, deactivate ${LIVE} `), run.out);
  // In order: the image copied, the template previewed and deployed, who ingress admits read back, the candidate awaited,
  // then probed.
  const order = [/^oras cp /, /^az deployment group what-if /, /^az deployment group create /, /ipSecurityRestrictions/, /^az containerapp revision show .*runningState/, /^node .*--origin/]
    .map((pattern) => indexOf(run.calls, pattern));
  assert.ok(order.every((at, index) => at >= 0 && (index === 0 || at > order[index - 1])), `${order}: ${run.calls.join("\n")}`);
});
