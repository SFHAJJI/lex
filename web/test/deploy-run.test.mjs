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
const releaseDirectory = mkdtempSync(join(tmpdir(), "lex-deploy-run-"));
after(() => rmSync(releaseDirectory, { recursive: true, force: true }));
writeFileSync(join(releaseDirectory, "release-manifest.json"), JSON.stringify({
  version: "v3-20261019T060000Z-0123456789ab",
  image: { manifest_digest: `sha256:${"a".repeat(64)}` },
  corpus: { sha256: "c".repeat(64) },
}));
const publicKey = join(releaseDirectory, "signing.pem");
writeFileSync(publicKey, "made up: the release read-back is the made-up node's\n");

const CANDIDATE = "ca-lex-v3--v3-20261019t060000z-0123456789ab";
const PRIVATE_RULES = '[{"name":"private-copy-1","ipAddressRange":"203.0.113.7/32","action":"Allow"}]';

// A legal page of the candidate's that gives an e-mail address in a mail link, and what the real check says of it
// (`legal-pages.mjs`): the owner's decision of 2026-10-19 keeps both off the site.
const CONTACT_PAGE = '<!doctype html><p>Contact: <a href="mailto:someone@example.invalid">someone@example.invalid</a>.</p>';
const [SHOWS_CONTACT] = legalPageTextFailures("/fr/legal-notice.html", CONTACT_PAGE);

// The made-up tools. `RULES` is what the app's ipSecurityRestrictions read answers ("[]" or "null" once the site is open, the
// private copy's rule before), `RULES_READ_FAILS` makes that read fail; `LEGAL_PAGE_FAILURE` is what the real check says of a
// candidate's legal page, which the made-up probe says as the real one does (`legalPageFailures`): with the token --opening,
// a failure, and the probe exits 1; without it, a note beside a pass.
const STUBS = `
  $global:calls = [System.Collections.Generic.List[string]]::new()
  function global:az {
    $line = $args -join ' '
    $global:LASTEXITCODE = 0
    if ($line -like 'deployment group create*') {
      $file = ($args | Where-Object { "$_".StartsWith('@') } | Select-Object -First 1).Substring(1)
      $global:calls.Add("az deployment group create openToPublic=$((Get-Content -Raw $file | ConvertFrom-Json).parameters.openToPublic.value)")
      return
    }
    $global:calls.Add("az $line")
    if ($line -like 'containerapp show*ipSecurityRestrictions*') {
      if ($env:RULES_READ_FAILS) { $global:LASTEXITCODE = 3; return }
      return $env:RULES
    }
    if ($line -like 'containerapp show*customDomains*') { return '[]' }
    if ($line -like 'containerapp revision show*runningState*') {
      return '{"running":"Running","health":"Healthy","provisioning":"Provisioned","active":true,"fqdn":"${CANDIDATE}.example.azurecontainerapps.io"}'
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

/** Runs deploy.ps1 with these parameters under the made-up tools: what it said, whether it threw, and every call it made. */
function deploy(parameters, env = {}) {
  const common = ["-Subscription", "s", "-ResourceGroup", "rg", "-EnvironmentId", "env", "-Registry", "crlex.azurecr.io", "-IdentityResourceId", "id",
    "-SigningPublicKey", quote(publicKey), "-Release", quote(releaseDirectory), "-LiveRevision", "ca-lex-v3--v3-live"];
  const script = `${STUBS}
    try { & ${quote(DEPLOY_SCRIPT)} ${[...common, ...parameters].join(" ")}; 'RESULT:returned' } catch { "RESULT:threw $($_.Exception.Message)" }
    foreach ($call in $global:calls) { "CALL:$call" }`;
  const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], {
    encoding: "utf8", env: { ...process.env, RULES: "", RULES_READ_FAILS: "", LEGAL_PAGE_FAILURE: "", ...env },
  });
  assert.equal(run.error, undefined, `pwsh could not run: ${run.error}`);
  const lines = run.stdout.split(/\r?\n/);
  const result = lines.find((line) => line.startsWith("RESULT:")) ?? `no result: ${run.stdout}${run.stderr}`;
  const calls = lines.filter((line) => line.startsWith("CALL:")).map((line) => line.slice("CALL:".length));
  const probes = calls.filter((call) => call.startsWith("node ")).map((call) => JSON.parse(call.slice("node ".length)))
    .filter((argv) => argv.includes("--origin"));
  return { out: run.stdout, result, calls, probes };
}

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
  assert.deepEqual(probe.slice(probe.indexOf("--origin"), probe.indexOf("--origin") + 2), ["--origin", `https://${CANDIDATE}.example.azurecontainerapps.io`]);
  assert.ok(run.calls.includes("az deployment group create openToPublic=True"), run.calls.join("\n"));
  assert.match(run.out, /--revision-weight ca-lex-v3--v3-20261019t060000z-0123456789ab=100/, "a candidate that passes is offered for promotion");
});

test("a -Public candidate whose legal page shows an e-mail address fails its probe, is deactivated, and is offered for no promotion (F1)", () => {
  assert.match(SHOWS_CONTACT, /^\/fr\/legal-notice\.html shows an e-mail address, a mail or telephone link, which the owner's decision of 2026-10-19 keeps off the site/);
  const run = deploy(["-SignerIdentity", "signer", "-Public", "-Apply"], { RULES: "[]", LEGAL_PAGE_FAILURE: SHOWS_CONTACT });
  assert.equal(run.result, "RESULT:threw The candidate failed its probe and was deactivated; no traffic moved.", run.out);
  assert.ok(run.out.includes(`- the legal pages are not ready to publish, so the site may not open: ${SHOWS_CONTACT}`), run.out);
  assert.ok(run.calls.includes(`az containerapp revision deactivate -g rg -n ca-lex-v3 --revision ${CANDIDATE}`), run.calls.join("\n"));
  assert.doesNotMatch(run.out, /--revision-weight/, "no promotion command is printed");
  // The private copy's candidate with the same page: its probe reads no --opening, so it says the page beside its pass, and
  // the candidate is kept and offered for promotion on the private copy.
  const privately = deploy(["-SignerIdentity", "signer", "-AllowedCidr", "203.0.113.7/32", "-Apply"], { RULES: PRIVATE_RULES, LEGAL_PAGE_FAILURE: SHOWS_CONTACT });
  assert.equal(privately.result, "RESULT:returned", privately.out);
  assert.ok(privately.out.includes(`(deploy.ps1 -Open and -Public refuse it): ${SHOWS_CONTACT}`), privately.out);
  assert.ok(!privately.calls.some((call) => call.startsWith("az containerapp revision deactivate")), privately.calls.join("\n"));
  assert.match(privately.out, /--revision-weight ca-lex-v3--v3-20261019t060000z-0123456789ab=100/);
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
  assert.ok(run.calls.includes("az deployment group create openToPublic=False"), run.calls.join("\n"));
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
