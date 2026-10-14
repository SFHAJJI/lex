// The deployment kit's static rules (`deploy/`), over the committed files, so they hold in CI where no Azure CLI or
// Bicep is installed (`deploy/validate.ps1` holds main.json to main.bicep's build offline). The kit is the owner's
// go-live as one command; these rules are what make it credential-free: no secret in the template or the script, the
// image pulled with a managed identity and named by digest, one server, and a new revision arriving with no traffic.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

const template = JSON.parse(readFileSync(new URL("../../deploy/main.json", import.meta.url), "utf8"));
const bicep = readFileSync(new URL("../../deploy/main.bicep", import.meta.url), "utf8");
const script = readFileSync(new URL("../../deploy/deploy.ps1", import.meta.url), "utf8");
// The script's code alone: its help block and comment lines describe what it does not do, in words these rules ban.
const code = script.replace(/<#[\s\S]*?#>/g, "").split("\n").filter((line) => !line.trim().startsWith("#")).join("\n");
const app = (Array.isArray(template.resources) ? template.resources : Object.values(template.resources))
  .find((resource) => resource.type === "Microsoft.App/containerApps");
const deployDirectory = fileURLToPath(new URL("../../deploy/", import.meta.url));

/** Runs a PowerShell script (pwsh, as CI has it on both its systems) and returns its standard output and exit code. */
function pwsh(script, env = {}) {
  const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], { encoding: "utf8", env: { ...process.env, ...env } });
  assert.equal(run.error, undefined, `pwsh could not run: ${run.error}`);
  return { out: run.stdout.trim(), err: run.stderr.trim(), status: run.status };
}

const quote = (text) => `'${text.replaceAll("'", "''")}'`;
const privateCopy = quote(`${deployDirectory}private-copy.ps1`);

test("the template holds no secret: no secure parameter, no secret, no key listed, no registry password", () => {
  for (const [name, parameter] of Object.entries(template.parameters)) {
    assert.ok(!/^secure/i.test(parameter.type), `parameter ${name} is ${parameter.type}`);
  }
  const text = JSON.stringify(template);
  for (const banned of ["listKeys", "listCredentials", "passwordSecretRef", "\"secrets\"", "secretRef", "@secure"]) {
    assert.ok(!text.includes(banned), `the template carries ${banned}`);
  }
  assert.ok(!bicep.includes("@secure"), "main.bicep declares a secure parameter");
});

test("the image is pulled with the user-assigned identity the owner names", () => {
  assert.equal(app.identity.type, "UserAssigned");
  assert.deepEqual(app.properties.configuration.registries, [{ server: "[parameters('registryServer')]", identity: "[parameters('identityResourceId')]" }]);
  assert.equal(app.properties.template.containers[0].image, "[parameters('image')]");
  assert.ok(!("env" in app.properties.template.containers[0]) || app.properties.template.containers[0].env.every((variable) => !("secretRef" in variable)));
});

test("one server, with a private writable /tmp, its probes on the image's own routes", () => {
  const { scale, volumes, containers } = app.properties.template;
  assert.deepEqual(scale, { minReplicas: 1, maxReplicas: 1 });
  assert.deepEqual(volumes, [{ name: "tmp", storageType: "EmptyDir" }]);
  assert.deepEqual(containers[0].volumeMounts, [{ volumeName: "tmp", mountPath: "/tmp" }]);
  assert.deepEqual(containers[0].probes.map((probe) => [probe.type, probe.httpGet.path, probe.httpGet.port]),
    [["Startup", "/", 8080], ["Liveness", "/", 8080], ["Readiness", "/evaluation-card.json", 8080]]);
  // The API listens only once its mount is verified: the startup probe gives it about four and a half minutes before the
  // container is restarted, at the probe's last failure (initial delay and every period but the last).
  const startup = containers[0].probes[0];
  assert.ok(startup.initialDelaySeconds + startup.periodSeconds * (startup.failureThreshold - 1) >= 270, "four and a half minutes to open the mount");
  // One vCPU: a replica's ephemeral storage holds the verified index copies only from one vCPU up (4 GiB; 2 GiB at half).
  assert.deepEqual(containers[0].resources, { cpu: "[json('1.0')]", memory: "2Gi" });
  // The replica's /tmp outlives a container restart: the API is told it is its own, and removes a previous start's copies.
  assert.ok(containers[0].env.some((variable) => variable.name === "LEX_V3_TEMP_IS_PRIVATE" && variable.value === "1"));
  assert.equal(app.properties.configuration.ingress.targetPort, 8080);
  assert.equal(app.properties.configuration.ingress.allowInsecure, false);
});

test("a new revision arrives as the candidate with no traffic while a live revision keeps it all", () => {
  const { activeRevisionsMode, ingress } = app.properties.configuration;
  assert.equal(activeRevisionsMode, "Multiple", "every revision is kept, so the live one serves while the candidate is probed");
  assert.match(ingress.traffic, /createObject\('revisionName', parameters\('liveRevision'\), 'weight', 100\), createObject\('latestRevision', true\(\), 'weight', 0, 'label', 'candidate'\)/);
});

test("until the owner opens the site, every deployment admits only the named ranges, and one that names none admits no one", () => {
  const { ingress } = app.properties.configuration;
  // Not only on a first deployment (the plan's item 0.5: a second deployment emptied the list and opened the private copy).
  assert.equal(ingress.ipSecurityRestrictions,
    "[if(parameters('openToPublic'), createArray(), if(empty(parameters('allowedSourceCidrs')), variables('admitNoOne'), variables('allowedOnly')))]");
  assert.ok(!/firstDeployment/.test(ingress.ipSecurityRestrictions), "who ingress admits does not depend on the deployment being the first");
  assert.equal(template.parameters.openToPublic.defaultValue, false, "the site is private unless the owner's step says otherwise");
  assert.deepEqual(template.parameters.allowedSourceCidrs.defaultValue, []);
  const [closed] = template.variables.admitNoOne;
  assert.deepEqual(closed, { name: "private-copy-closed", description: closed.description, ipAddressRange: "192.0.2.1/32", action: "Allow" }, "with no range named, a range that is never routed: closed, not open");
  const allowed = template.variables.copy.find((copy) => copy.name === "allowedOnly");
  assert.equal(allowed.count, "[length(parameters('allowedSourceCidrs'))]");
  assert.equal(allowed.input.ipAddressRange, "[parameters('allowedSourceCidrs')[copyIndex('allowedOnly')]]");
  assert.equal(allowed.input.action, "Allow");
});

test("who ingress admits is held exactly: the named ranges, each allowed, or everyone only after the opening", () => {
  const cases = [
    ["the named ranges", '[{"ipAddressRange":"203.0.113.7/32","action":"Allow"},{"ipAddressRange":"198.51.100.0/24","action":"Allow"}]', "203.0.113.7/32,198.51.100.0/24", false, ""],
    ["one more rule than named", '[{"ipAddressRange":"203.0.113.7/32","action":"Allow"},{"ipAddressRange":"192.0.2.9/32","action":"Allow"}]', "203.0.113.7/32", false, "not exactly"],
    ["a named range missing", '[{"ipAddressRange":"203.0.113.7/32","action":"Allow"}]', "203.0.113.7/32,198.51.100.0/24", false, "not exactly"],
    ["a deny rule", '[{"ipAddressRange":"203.0.113.7/32","action":"Deny"}]', "203.0.113.7/32", false, "not exactly"],
    ["no rule at all (null)", "null", "203.0.113.7/32", false, "admits everyone"],
    ["no rule at all ([])", "[]", "203.0.113.7/32", false, "admits everyone"],
    ["after the opening, none", "[]", "", true, ""],
    ["after the opening, one left", '[{"ipAddressRange":"203.0.113.7/32","action":"Allow"}]', "", true, "still admits only"],
  ];
  for (const [what, rules, allowed, open, failure] of cases) {
    const { out, status } = pwsh(`. ${privateCopy}; $f = Get-AdmittedFailure ${quote(rules)} (Get-AllowedCidrs ${quote(allowed)}) $${open}; if ($f) { "FAIL:$f" } else { "HOLDS" }`);
    assert.equal(status, 0, what);
    if (failure === "") assert.equal(out, "HOLDS", what);
    else assert.ok(out.startsWith("FAIL:") && out.includes(failure), `${what}: ${out}`);
  }
});

test("each range is an address or a small network, and several may be named in the form the runbook uses", () => {
  const { out } = pwsh(`. ${privateCopy}; (Get-AllowedCidrs @('203.0.113.7/32, 198.51.100.0/24', '192.0.2.1/32')) -join '|'`);
  assert.equal(out, "203.0.113.7/32|198.51.100.0/24|192.0.2.1/32", "a comma-separated value, as pwsh -File hands it over, names each range");
  for (const [cidr, refused] of [["203.0.113.7/32", false], ["203.0.113.0/24", false], ["203.0.112.0/23", true], ["0.0.0.0/0", true], ["203.0.113.7", true], ["203.0.113.256/32", true]]) {
    const { out: verdict } = pwsh(`. ${privateCopy}; $f = Get-AllowedCidrFailure ${quote(cidr)}; if ($f) { 'REFUSED' } else { 'ADMITTED' }`);
    assert.equal(verdict, refused ? "REFUSED" : "ADMITTED", cidr);
  }
});

test("the opening removes every address rule, reads back that ingress admits everyone, and only then says so", () => {
  // The Azure CLI, made up: the listing fails or names two rules; the read-back says what is left.
  const stub = `
    $global:calls = @()
    function global:az { $line = $args -join ' '; $global:calls += $line; $global:LASTEXITCODE = 0
      if ($line -like 'containerapp ingress access-restriction list*') { if ($env:LIST_FAILS) { $global:LASTEXITCODE = 3; return }; return @('private-copy-1', 'private-copy-2') }
      if ($line -like 'containerapp show*') { if ($env:STILL_RESTRICTED) { return '[{"ipAddressRange":"203.0.113.7/32","action":"Allow"}]' }; return '[]' }
    }
    try { & ${quote(`${deployDirectory}deploy.ps1`)} -Subscription s -ResourceGroup rg -Open 6>$null; 'RESULT:returned' } catch { "RESULT:threw $($_.Exception.Message)" }
    'REMOVED:' + (@($global:calls | Where-Object { $_ -like '*access-restriction remove*' }).Count)`;
  const opened = pwsh(stub);
  assert.match(opened.out, /RESULT:returned/);
  assert.match(opened.out, /REMOVED:2/, "both rules removed");

  const unlisted = pwsh(stub, { LIST_FAILS: "1" });
  assert.match(unlisted.out, /RESULT:threw The address rules of ca-lex-v3 could not be listed; nothing was removed/);
  assert.match(unlisted.out, /REMOVED:0/);

  const restricted = pwsh(stub, { STILL_RESTRICTED: "1" });
  assert.match(restricted.out, /RESULT:threw The opening did not take: ingress still admits only 203\.0\.113\.7\/32/);
});

test("the script deploys the private copy with its ranges every time, reads them back, and opens the site only on the owner's step", () => {
  // Every deployment names its ranges, or says -Public after the opening; never neither, never both.
  assert.match(code, /if \(-not \$Public -and \$AllowedCidr\.Count -eq 0\) \{\s*throw "Until the opening every deployment names the address ranges/);
  assert.match(code, /if \(\$Public -and \$AllowedCidr\.Count -gt 0\) \{\s*throw/);
  assert.match(code, /allowedSourceCidrs = @\(\$AllowedCidr\); openToPublic = \[bool\]\$Public/);
  assert.ok(!/ProbeSourceCidr|probeSourceCidr/.test(script), "the first-deployment-only parameter is gone");
  // Read back before the probe, and stop if the ranges do not hold.
  const readBack = code.indexOf('Step "read back who ingress admits"');
  assert.ok(readBack > code.indexOf("az deployment group create") && readBack < code.indexOf('Step "probe the candidate"'), "read back after the deployment, before the probe");
  assert.match(code, /if \(\$failure\) \{ throw "Who ingress admits is not what this deployment names/);
  // The read-back and its repair change Azure only under -Apply, and the deployment hands the template its parameters
  // only through the parameters file (review of #960).
  const applied = code.slice(code.indexOf("if ($Apply) {\n    Step \"read back who ingress admits\""));
  assert.ok(applied.length > 0 && applied.indexOf("access-restriction set") < applied.indexOf("if ($failure) { throw"), "the repair stands inside -Apply's block");
  assert.match(code, /az deployment group create -g \$ResourceGroup -f \(Join-Path \$checkout 'deploy\/main\.bicep'\) -p "@\$parametersFile" -o none\n/);
  assert.match(code, /\$AllowedCidr = Get-AllowedCidrs \$AllowedCidr/);
  // Removing the ranges is the owner's -Open alone: nowhere else does the script remove one, and promotion stays printed.
  const opening = code.slice(code.indexOf("if ($Open) {"), code.indexOf("exit 0", code.indexOf("if ($Open) {")));
  assert.match(opening, /az containerapp ingress access-restriction remove -g \$ResourceGroup -n \$AppName --rule-name \$rule/);
  const elsewhere = code.replace(opening, "").split("\n").filter((line) => line.includes("access-restriction remove"));
  assert.ok(elsewhere.every((line) => line.trim().startsWith("Write-Host")), `a range removed outside the opening: ${elsewhere.join(" | ")}`);
  assert.match(script, /\[Parameter\(Mandatory, ParameterSetName = 'Open'\)\] \[switch\] \$Open/);
});

test("the script never logs in, never handles a secret on a command line or on disk, and never promotes", () => {
  for (const banned of [/\baz login\b/, /Connect-AzAccount/, /ConvertTo-SecureString/, /--password(?!-stdin)\b/, /-p\s+\S*password/i, /Set-Content[^\n]*token/i, /Out-File[^\n]*token/i]) {
    assert.ok(!banned.test(code), `deploy.ps1 matches ${banned}`);
  }
  // The registry token goes on standard input to `oras login`, into a registry config in a fresh private directory that
  // `oras cp` reads and a finally block removes (oras cp takes no password on stdin; review of #890).
  assert.match(code, /--expose-token --query accessToken -o tsv \|\s*\n?\s*oras login \$Registry --username 0{8}-0{4}-0{4}-0{4}-0{12} --password-stdin --registry-config \$registryConfig/);
  assert.match(code, /oras cp --from-oci-layout "\$\{archive\}@\$digest" "\$Registry\/\$\{Repository\}:\$tag" --to-registry-config \$registryConfig/);
  assert.match(code, /finally \{\s*if \(Test-Path \$session\) \{ Remove-Item -Recurse -Force \$session/);
  assert.ok(!/--to-password|--password(?!-stdin)/.test(code), "no password on a command line");
  // Promotion is printed for the owner, never run: the traffic commands appear only inside Write-Host. (Opening the site,
  // the owner's -Open, is held below.)
  const promotion = script.split("\n").filter((line) => line.includes("ingress traffic set"));
  assert.ok(promotion.length >= 2 && promotion.every((line) => line.trim().startsWith("Write-Host")), `promotion runs: ${promotion.join(" | ")}`);
  // Without -Apply nothing runs: every Azure or registry command is inside Run, which only shows it unless -Apply (or the
  // removal step or the opening, each its own parameter set).
  assert.match(script, /if \(\$Apply -or \$Remove -or \$Open\) \{\s*& \$command/);
});

test("the script deploys only a release that verifies, and only by the image's digest", () => {
  const deploying = code.slice(code.indexOf("Step \"verify the release"));
  const verify = deploying.indexOf("deploy-probe.mjs') --release");
  const firstAzure = deploying.search(/Run "az /);
  assert.ok(verify > 0 && verify < firstAzure, "the release is read back under the signing identity's key before any Azure command");
  assert.match(script, /\$image = "\$Registry\/\$Repository@\$digest"/);
  assert.match(script, /\$digest -notmatch '\^sha256:\[0-9a-f\]\{64\}\$'/);
  assert.match(script, /image = \$image;/);
  // The browser probes run unless -SkipBrowserProbe says they do not, and a skip is stated.
  assert.match(code, /\$browserProbe = if \(\$SkipBrowserProbe\) \{ @\(\) \} else \{ @\('--browser'\) \}/);
  assert.match(code, /deploy-probe\.mjs'\) --origin \$origin --release \$releasePath --public-key \$keyPath @signerProbe @browserProbe/);
  // A deployment applies only a release the release signer signed: -Apply requires -SignerIdentity, which both read-backs
  // carry, so a rehearsal release is read in a plan run only.
  assert.match(script, /if \(\$Apply -and \[string\]::IsNullOrWhiteSpace\(\$SignerIdentity\)\) \{\s*throw "-Apply deploys a release signed by the release signer/);
  // What both read-backs carry is the signer, and the guard stands before the release is touched (review of #931: an
  // emptied $signerProbe, or the guard moved after the deployment, left every rule above green).
  assert.match(code, /\$signerProbe = if \(\[string\]::IsNullOrWhiteSpace\(\$SignerIdentity\)\) \{ @\(\) \} else \{ @\('--signer', \$SignerIdentity\) \}/);
  const guard = code.indexOf('throw "-Apply deploys');
  assert.ok(guard > 0 && guard < code.indexOf('Step "verify the release'), "the -Apply guard runs before the release is read back or any Azure command");
  assert.match(code, /deploy-probe\.mjs'\) --release \$releasePath --public-key \$keyPath @signerProbe\n/);
  assert.match(code, /the browser probes are skipped \(-SkipBrowserProbe\)/);
  // Rollback and forward again are printed for the owner with promotion, never run.
  assert.match(code, /Write-Host "  > az containerapp ingress traffic set -g \$ResourceGroup -n \$AppName --revision-weight \$LiveRevision=100"/);
  // A failed probe deactivates the candidate (the removal step) and stops.
  assert.match(script, /if \(\$LASTEXITCODE -ne 0\) \{[\s\S]*?revision deactivate[\s\S]*?throw "The candidate failed its probe/);
});
