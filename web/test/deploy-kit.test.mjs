// The deployment kit's static rules (`deploy/`), over the committed files, so they hold in CI where no Azure CLI or
// Bicep is installed (`deploy/validate.ps1` holds main.json to main.bicep's build offline). The kit is the owner's
// go-live as one command; these rules are what make it credential-free: no secret in the template or the script, the
// image pulled with a managed identity and named by digest, one server, and a new revision arriving with no traffic.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
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

test("one server, with a private writable /tmp, its probes on the image's own routes, ready only with the law mounted", () => {
  const { scale, volumes, containers } = app.properties.template;
  assert.deepEqual(scale, { minReplicas: 1, maxReplicas: 1 });
  assert.deepEqual(volumes, [{ name: "tmp", storageType: "EmptyDir" }]);
  assert.deepEqual(containers[0].volumeMounts, [{ volumeName: "tmp", mountPath: "/tmp" }]);
  // A release's revision is probed as it always was; its maintenance revision asks its readiness alone (held below).
  assert.equal(containers[0].probes, "[if(parameters('maintenance'), variables('maintenanceProbes'), variables('releaseProbes'))]");
  const probes = template.variables.releaseProbes;
  assert.deepEqual(probes.map((probe) => [probe.type, probe.httpGet.path, probe.httpGet.port]),
    [["Startup", "/", 8080], ["Liveness", "/", 8080], ["Readiness", "/health/ready", 8080]]);
  // The API listens only once its mount is verified: the startup probe gives it about four and a half minutes before the
  // container is restarted, at the probe's last failure (initial delay and every period but the last).
  const startup = probes[0];
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

test("the release's maintenance revision is the same image with LEX_V3_MAINTENANCE=on, probed on its readiness, and never takes traffic as it deploys", () => {
  // R60, R108, R115; the plan's item 10.14. Off by default: every other revision is a release's, as before.
  assert.equal(template.parameters.maintenance.type, "bool");
  assert.equal(template.parameters.maintenance.defaultValue, false);
  const [container] = app.properties.template.containers;
  assert.equal(container.image, "[parameters('image')]", "the release's own image, by digest, as any revision");
  // The variable the API reads, and its one value that switches it on, are the API's own (V3WebRoot.Maintenance.cs).
  const api = readFileSync(new URL("../../src/Lex.V3.Api/V3WebRoot.Maintenance.cs", import.meta.url), "utf8");
  const variable = /internal const string MaintenanceVariable = "([^"]+)";/.exec(api)?.[1];
  const on = /internal const string MaintenanceOn = "([^"]+)";/.exec(api)?.[1];
  assert.deepEqual([variable, on], ["LEX_V3_MAINTENANCE", "on"]);
  assert.deepEqual(container.env.filter((entry) => entry.name === variable), [{ name: variable, value: `[if(parameters('maintenance'), '${on}', 'off')]` }],
    "on only for the maintenance revision; every other revision says off, which the API reads as off");
  // It mounts nothing, so it listens within seconds, and its `/` is the maintenance page (503): each probe asks readiness,
  // which is 204 only while it holds a maintenance page in each language, and soon.
  const probes = template.variables.maintenanceProbes;
  assert.deepEqual(probes.map((probe) => [probe.type, probe.httpGet.path, probe.httpGet.port]),
    [["Startup", "/health/ready", 8080], ["Liveness", "/health/ready", 8080], ["Readiness", "/health/ready", 8080]]);
  assert.ok(probes.every((probe) => probe.initialDelaySeconds <= 5), "short delays: there is no mount to wait for");
  const [startup] = probes;
  assert.ok(startup.initialDelaySeconds + startup.periodSeconds * (startup.failureThreshold - 1) <= 60, "a minute to start, not four and a half");
  // The traffic: the live revision keeps 100 per cent and the new revision has none, labelled maintenance; never the
  // first-deployment rule that sends all traffic to the latest revision.
  const { traffic } = app.properties.configuration.ingress;
  const branch = "[if(parameters('maintenance'), createArray(createObject('revisionName', parameters('liveRevision'), 'weight', 100), createObject('latestRevision', true(), 'weight', 0, 'label', 'maintenance')), if(variables('firstDeployment'), ";
  assert.ok(traffic.startsWith(branch), traffic);
  assert.equal((traffic.match(/'label', 'maintenance'/g) ?? []).length, 1);
  // Everything else is the release's revision's: the same resources, volume, scale, registry and ingress rules.
  assert.deepEqual(container.resources, { cpu: "[json('1.0')]", memory: "2Gi" });
  assert.ok(!/maintenance/.test(JSON.stringify(app.properties.configuration.ingress.ipSecurityRestrictions)));

  // The kit: -Maintenance in the deployment's parameter set; refused without -LiveRevision (never a first deployment) or with
  // the browser probes, before the release is read back; its settings name it, so its revision is never the candidate's;
  // the template told maintenance.
  assert.match(code, /\[Parameter\(ParameterSetName = 'Deploy'\)\] \[switch\] \$Maintenance,/);
  const refused = code.indexOf("if ($Maintenance -and [string]::IsNullOrEmpty($LiveRevision)) {");
  assert.ok(refused > 0 && refused < code.indexOf('Step "verify the release'), "refused before anything is read or changed");
  assert.match(code, /if \(\$Maintenance -and \$BrowserProbe\) \{ throw /);
  assert.match(code, /if \(\$Maintenance\) \{ \$revisionSettings\['maintenance'\] = \$true \}\n\$suffix = Get-RevisionSuffix /);
  assert.match(code, /maintenance = \[bool\]\$Maintenance\n/, "the template's parameter");
  // Its probe: at its own address, under the trap, held to the release's maintenance pages; a failure deactivates it and
  // stops; a pass deactivates it too, through the same guard, and prints the switch, which is never run.
  const branchCode = code.slice(code.indexOf("if ($Maintenance) {\n    & node @nodeTrap"), code.indexOf('if (-not $BrowserProbe) { Write-Host "  no browser probe (no -BrowserProbe)'));
  assert.match(branchCode, /^if \(\$Maintenance\) \{\n    & node @nodeTrap \(Join-Path \$checkout 'web\/scripts\/deploy-probe\.mjs'\) --maintenance --origin \$origin --release \$releasePath --public-key \$keyPath @signerProbe\n    if \(\$LASTEXITCODE -ne 0\) \{\n        Disable-Candidate 'the maintenance probe failed'\n/);
  assert.match(branchCode, /\n    Disable-Candidate 'the maintenance revision answers as its release holds, and is kept inactive until it is switched on'\n    if \(\$LASTEXITCODE -ne 0\) \{ throw /);
  assert.match(branchCode, /\n    Write-MaintenanceSwitch\n    exit 0\n\}\n$/, "and exits before anything a candidate's probe says");
  assert.ok(code.indexOf("$origin = \"https://$($state.fqdn)\"") < code.indexOf(branchCode), "after the wait for the revision's own address");
  const switching = code.slice(code.indexOf("function Write-MaintenanceSwitch {"), code.indexOf("\n}\n", code.indexOf("function Write-MaintenanceSwitch {")));
  const commands = switching.split("\n").filter((line) => line.includes("> az "));
  assert.deepEqual(commands.map((line) => line.trim().replace(/^Write-Host "  > (.*)"$/, "$1")), [
    "az containerapp revision activate -g $ResourceGroup -n $AppName --revision $candidate",
    "az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $candidate=100",
    "az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $LiveRevision=100",
    "az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate",
  ], "switch on, then off: printed");
  assert.ok(switching.split("\n").slice(1).every((line) => line.trim().startsWith("Write-Host")), "and nothing in it runs");
  // Every deployment says how to pre-build its release's maintenance revision (R60), naming the revision serving as it is
  // printed and never the candidate not yet promoted (the review of writer/screens-gaps, F1).
  assert.match(code, /-LiveRevision \$servingNow -Maintenance -Apply"\n/);
  assert.doesNotMatch(code, /-LiveRevision \$candidate -Maintenance/);
  // And every redeployment reads the traffic before anything in Azure changes: -LiveRevision must hold all of it.
  const trafficRead = code.indexOf("properties.configuration.ingress.traffic");
  assert.ok(trafficRead > 0 && trafficRead < code.indexOf("oras cp") && trafficRead < code.indexOf("az deployment group create"), "the traffic is read before the image is copied or the template deployed");
  assert.match(code, /if \(\$serving -ne \$LiveRevision\) \{/);
});

test("the candidate is probed at its own address, once it is ready, never through the candidate label", () => {
  // The label names the latest *ready* revision: probed through it earlier, the live revision answered (2026-10-18); and
  // the card cannot tell two revisions apart in general, so only the revision's own address proves which one answered.
  assert.match(code, /\$candidate = "\$AppName--\$suffix"/, "the candidate is the revision this deployment made, by name");
  assert.doesNotMatch(code, /---candidate\.\$domain/, "the probe never goes through the candidate label");
  assert.match(code, /\$origin = "https:\/\/\$\(\$state\.fqdn\)"/, "the probe asks the revision's own address");
  const wait = code.indexOf("fqdn:properties.fqdn");
  const probe = code.indexOf("& node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --origin $origin");
  assert.ok(wait > 0 && probe > wait, "the wait for the revision comes before its probe");
  assert.match(code, /"\$\(\$state\.running\)"\.StartsWith\('Running'\)/);
  assert.match(code, /\$state\.health -eq 'Healthy'/);
  assert.match(code, /"\$\(\$state\.provisioning\)" -eq 'Failed' -or "\$\(\$state\.running\)" -eq 'Failed'/, "a failed revision ends the wait at once");
  assert.match(code, /\$state\.active -eq \$false/, "an inactive revision ends the wait at once");
  assert.match(code, /if \(\(Get-Date\) -gt \$readyBy\) \{\s*Disable-Candidate 'the candidate did not become ready'\s*throw /,
    "a candidate that never becomes ready is deactivated, and no traffic moves");
});

test("a deployment deactivates only the revision it made: every deactivation is the candidate's, behind one guard read before Azure changes", () => {
  // The incident of 2026-10-27: a deployment Azure refused (its suffix was the live revision's) had its failure step
  // deactivate the revision of that name, the live one. deploy-run.test.mjs runs each path; this keeps the shape.
  const removal = code.slice(code.indexOf("if ($Remove) {"), code.indexOf("exit 0", code.indexOf("if ($Remove) {")));
  const deactivations = code.replace(removal, "").split("\n").filter((line) => line.includes("revision deactivate") && !line.trim().startsWith("Write-Host"));
  assert.deepEqual(deactivations.map((line) => line.trim()), ["az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate"],
    "outside -Remove, the one deactivation is the guard's, of the candidate");
  const guard = code.slice(code.indexOf("function Disable-Candidate("), code.indexOf("az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate"));
  assert.match(guard, /^function Disable-Candidate\(\[string\] \$Why, \[switch\] \$IfActive\) \{\s*if \(-not \$candidateIsOurs\) \{\n/, "the guard's first question");
  assert.match(guard.slice(0, guard.indexOf("if ($IfActive)")), /\n\s*return\n\s*\}\s*$/, "nothing is deactivated unless the candidate is this deployment's");
  assert.match(code, /\$candidateIsOurs = \$null -ne \$priorRevisions -and \$priorRevisions -notcontains \$candidate -and \$candidate -ne \$LiveRevision\n/);
  assert.equal((code.match(/^\s*Disable-Candidate '/gm) ?? []).length, 6,
    "the failed deployment, the candidate that fails or is never ready, the failed probe, and the maintenance revision's failed probe and its keeping inactive");
  // The revisions, every inactive one too, are read and a name the app holds (or -LiveRevision's) refused before the image
  // is copied, the template previewed or anything else in Azure changes.
  const read = code.indexOf('az containerapp revision list -g $ResourceGroup -n $AppName --all --query "[].name" -o tsv');
  const refused = code.indexOf('throw "The revision $candidate already exists in $AppName');
  assert.ok(read > 0 && read < refused && refused < code.indexOf('Step "copy the image') && refused < code.indexOf("az deployment group what-if"), "read and refused before any change");
  assert.match(code, /\$held = @\(@\(\$priorRevisions\) \+ @\(\$LiveRevision\) \| Where-Object \{ \$_ \}\)\nif \(\$held -contains \$candidate\) \{/);
  assert.match(code, /elseif \(-not \[string\]::IsNullOrEmpty\(\$LiveRevision\)\) \{\s*throw "The revisions of \$AppName could not be read; nothing was deployed/, "a read that fails stops a redeployment");
  // The suffix names the release and a digest of what makes the revision, so the same release with another setting is a
  // revision of its own.
  assert.match(code, /\$revisionSettings = \[ordered\]@\{\s*image = \$image; searchEngines = \$SearchEngines; openToPublic = \[bool\]\$Public; allowedSourceCidrs = @\(\$AllowedCidr\)\s*template = /);
  assert.match(code, /\$suffix = Get-RevisionSuffix \$manifest\.version \$AppName \$revisionSettings\n\$candidate = "\$AppName--\$suffix"/);
  assert.match(code, /revisionSuffix = \$suffix;/, "the template is handed that suffix");
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

test("search engines are kept out unless the deployment says indexed, and the API reads the switch the template sets", () => {
  // The owner's answer to O27 on 2026-10-18 is yes; the switch is set at the opening, never by default.
  assert.deepEqual(template.parameters.searchEngines.allowedValues, ["kept-out", "indexed"]);
  assert.equal(template.parameters.searchEngines.defaultValue, "kept-out");
  const env = app.properties.template.containers[0].env;
  assert.ok(env.some((variable) => variable.name === "LEX_V3_SEARCH_ENGINES" && variable.value === "[parameters('searchEngines')]"));
  assert.match(code, /\[ValidateSet\('kept-out', 'indexed', IgnoreCase = \$false\)\] \[string\] \$SearchEngines = 'kept-out'/);
  assert.match(code, /searchEngines = \$SearchEngines/);
});

test("a redeployment keeps the custom domain bound to the app, since the template would otherwise set none", () => {
  const { ingress } = app.properties.configuration;
  assert.equal(ingress.customDomains, "[parameters('customDomains')]");
  assert.deepEqual(template.parameters.customDomains.defaultValue, []);
  assert.match(code, /az containerapp show -g \$ResourceGroup -n \$AppName --query "properties\.configuration\.ingress\.customDomains"/,
    "the domains bound now are read back from the app");
  assert.match(code, /customDomains = @\(\$customDomains\)/, "and passed to the template");
  assert.match(code, /if \(\$LASTEXITCODE -ne 0\) \{\s*if \(-not \[string\]::IsNullOrEmpty\(\$LiveRevision\)\) \{\s*throw "The custom domains bound to \$AppName could not be read/,
    "a failed read stops a redeployment instead of unbinding the domain (the review of #988)");
  assert.match(code, /throw "The deployment unbound \$\(\$lost -join ', '\)/, "and the domains are read back after it");
  assert.match(code, /\[ValidateSet\('kept-out', 'indexed', IgnoreCase = \$false\)\]/, "the switch's value is exact, as the API reads it");
  const bind = readFileSync(new URL("../../deploy/bind-domain.ps1", import.meta.url), "utf8").replace(/<#[\s\S]*?#>/g, "");
  assert.match(bind, /Resolve-DnsName \$Hostname -Type CNAME -Server \$Resolver/, "the binding waits for public DNS to answer the CNAME");
  assert.match(bind, /--validation-method CNAME/, "a managed certificate, validated by the CNAME");
  assert.match(bind, /if \(-not \$Apply\) \{ Write-Host "== planned only/, "a plan run binds nothing");
  assert.doesNotMatch(bind, /ipSecurityRestrictions|ingress access-restriction|-Open\b/, "binding the name never opens the site");
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
  // The Azure CLI, made up: the app's address, or none; the listing fails or names two rules; the read-back says what is
  // left. And the opening's check of the legal pages (`deploy-probe.mjs --opening`), made up as the node it runs: its exit
  // code is the check's verdict (the check itself is held in deploy-probe.test.mjs).
  const stub = `
    $global:calls = @()
    function global:az { $line = $args -join ' '; $global:calls += $line; $global:LASTEXITCODE = 0
      if ($line -like 'containerapp show*ingress.fqdn*') { if ($env:NO_FQDN) { $global:LASTEXITCODE = 3; return }; return 'ca-lex-v3.example.azurecontainerapps.io' }
      if ($line -like 'containerapp ingress access-restriction list*') { if ($env:LIST_FAILS) { $global:LASTEXITCODE = 3; return }; return @('private-copy-1', 'private-copy-2') }
      if ($line -like 'containerapp show*') { if ($env:STILL_RESTRICTED) { return '[{"ipAddressRange":"203.0.113.7/32","action":"Allow"}]' }; return '[]' }
    }
    function global:node { $global:calls += "node $($args -join ' ')"; $global:LASTEXITCODE = [int]($env:OPENING_EXIT ?? '0') }
    try { & ${quote(`${deployDirectory}deploy.ps1`)} -Subscription s -ResourceGroup rg -Open 6>$null; 'RESULT:returned' } catch { "RESULT:threw $($_.Exception.Message)" }
    'REMOVED:' + (@($global:calls | Where-Object { $_ -like '*access-restriction remove*' }).Count)
    'CALLS:' + ($global:calls -join ' | ')`;
  const opened = pwsh(stub);
  assert.match(opened.out, /RESULT:returned/);
  assert.match(opened.out, /REMOVED:2/, "both rules removed");
  // The legal pages are asked first, at the app's own address, under the browser trap, before any rule is listed or removed.
  const calls = /CALLS:(.*)/.exec(opened.out)[1];
  assert.match(calls, /node --import file:\/\/\/\S+browser-trap\.mjs \S+deploy-probe\.mjs --opening --origin https:\/\/ca-lex-v3\.example\.azurecontainerapps\.io/);
  assert.ok(calls.indexOf("--opening") < calls.indexOf("access-restriction list"), `the check runs before the rules are touched: ${calls}`);

  // A legal page that shows an e-mail address, a contact link, a placeholder or a postcode, or cannot be read: the check
  // fails, and the site stays private.
  const unready = pwsh(stub, { OPENING_EXIT: "1" });
  assert.match(unready.out, /RESULT:threw The legal pages ca-lex-v3 serves are not ready to publish \(above\); nothing was removed, and the site stays private/);
  assert.match(unready.out, /REMOVED:0/);
  assert.doesNotMatch(unready.out, /access-restriction list/, "no rule is even listed");
  // An address that cannot be read: nothing is asked, nothing removed.
  const unread = pwsh(stub, { NO_FQDN: "1" });
  assert.match(unread.out, /RESULT:threw The address of ca-lex-v3 could not be read, so its legal pages could not be checked; nothing was removed/);
  assert.match(unread.out, /REMOVED:0/);
  assert.doesNotMatch(unread.out, /--opening/);

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

test("no script of the kit or of eng/ takes an argument list from an if whose branch is an array: PowerShell unrolls it", () => {
  // `$x = if (...) { @('--opening') } else { @() }` is the string '--opening' (or $null), and `@x` hands a string to a command
  // one character at a time: deploy.ps1's probe never read --opening (the review of #989). The form is `@(if (...) { ... })`.
  // deploy-run.test.mjs runs the script whole and reads the probe's arguments; this keeps the form out of every script.
  const roots = ["../../deploy/", "../../eng/"].map((path) => new URL(path, import.meta.url));
  const scripts = roots.flatMap((root) => readdirSync(root).filter((name) => name.endsWith(".ps1")).map((name) => [`${fileURLToPath(root)}${name}`, new URL(name, root)]));
  assert.ok(scripts.length >= 8, `the scripts are read: ${scripts.map(([name]) => name).join(", ")}`);
  for (const [name, url] of scripts) {
    // The code alone, comments and help blocks out, read across lines: the branch's array may stand on the next line.
    const scriptCode = readFileSync(url, "utf8").replace(/<#[\s\S]*?#>/g, "").split(/\r?\n/).filter((line) => !line.trim().startsWith("#")).join("\n");
    const offenders = [...scriptCode.matchAll(/=[ \t]*if[ \t]*\([^\n]*\)\s*\{\s*@\(/g)].map((match) => match[0]);
    assert.deepEqual(offenders, [], name);
  }
});

test("the script deploys only a release that verifies, and only by the image's digest", () => {
  const deploying = code.slice(code.indexOf("Step \"verify the release"));
  const verify = deploying.indexOf("deploy-probe.mjs') --release");
  const firstAzure = deploying.search(/Run "az /);
  assert.ok(verify > 0 && verify < firstAzure, "the release is read back under the signing identity's key before any Azure command");
  assert.match(script, /\$image = "\$Registry\/\$Repository@\$digest"/);
  assert.match(script, /\$digest -notmatch '\^sha256:\[0-9a-f\]\{64\}\$'/);
  assert.match(script, /image = \$image;/);
  // No browser runs unless -BrowserProbe asks for one (the review of #979: an -Apply that left out -SkipBrowserProbe started
  // a browser on a machine where none may run); -SkipBrowserProbe stays accepted, a no-op, and the two together are refused.
  assert.match(code, /\[Parameter\(ParameterSetName = 'Deploy'\)\] \[switch\] \$BrowserProbe,/);
  assert.match(code, /\[Parameter\(ParameterSetName = 'Deploy'\)\] \[switch\] \$SkipBrowserProbe,/);
  // Not `$browserProbe`: PowerShell's names are not case-sensitive, so that array would be the -BrowserProbe switch itself.
  assert.match(code, /\$browserArgs = @\(if \(\$BrowserProbe\) \{ '--browser' \}\)/);
  assert.ok(!/\$browserProbe\b/.test(code), "no variable spelled like the switch");
  assert.match(code, /if \(\$BrowserProbe -and \$SkipBrowserProbe\) \{ throw /);
  assert.equal((code.match(/\$SkipBrowserProbe/g) ?? []).length, 2, "-SkipBrowserProbe is declared and refused beside -BrowserProbe, and decides nothing else");
  assert.match(code, /deploy-probe\.mjs'\) --origin \$origin --release \$releasePath --public-key \$keyPath @signerProbe @browserArgs /);
  // Without -BrowserProbe both probes run with the browser trap loaded first, so nothing they run can find or start one.
  assert.match(code, /\$trapPath = \[System\.IO\.Path\]::GetFullPath\(\(Join-Path \$checkout 'web\/scripts\/browser-trap\.mjs'\)\)/);
  assert.match(code, /if \(-not \$trapUrl -or -not \$trapUrl\.StartsWith\('file:\/\/\/'\)\) \{ throw /, "an empty or non-file trap URL stops the deployment");
  assert.match(code, /\$nodeTrap = @\(if \(-not \$BrowserProbe\) \{ '--import', \$trapUrl \}\)/);
  assert.equal((code.match(/& node @nodeTrap \(Join-Path \$checkout 'web\/scripts\/deploy-probe\.mjs'\)/g) ?? []).length, 4,
    "both probes, the maintenance revision's probe and the opening's check of the legal pages run under the trap");
  assert.equal((code.match(/Join-Path \$checkout 'web\/scripts\/deploy-probe\.mjs'\)/g) ?? []).length, 4, "and the probe runs nowhere else");
  // Once the site is open, a -Public deployment's probe holds the candidate's legal pages to the opening's rule; and every
  // candidate's probe says whether promotion refuses its data's age (`--promotion-check`, exit 3).
  assert.match(code, /\$openingArgs = @\(if \(\$Public\) \{ '--opening' \}\)/);
  assert.match(code, /deploy-probe\.mjs'\) --origin \$origin --release \$releasePath --public-key \$keyPath @signerProbe @browserArgs @openingArgs --promotion-check --search-engines \$SearchEngines\n/);
  // A deployment applies only a release the release signer signed: -Apply requires -SignerIdentity, which both read-backs
  // carry, so a rehearsal release is read in a plan run only.
  assert.match(script, /if \(\$Apply -and \[string\]::IsNullOrWhiteSpace\(\$SignerIdentity\)\) \{\s*throw "-Apply deploys a release signed by the release signer/);
  // What both read-backs carry is the signer, and the guard stands before the release is touched (review of #931: an
  // emptied $signerProbe, or the guard moved after the deployment, left every rule above green).
  assert.match(code, /\$signerProbe = @\(if \(-not \[string\]::IsNullOrWhiteSpace\(\$SignerIdentity\)\) \{ '--signer', \$SignerIdentity \}\)/);
  const guard = code.indexOf('throw "-Apply deploys');
  assert.ok(guard > 0 && guard < code.indexOf('Step "verify the release'), "the -Apply guard runs before the release is read back or any Azure command");
  assert.match(code, /deploy-probe\.mjs'\) --release \$releasePath --public-key \$keyPath @signerProbe\n/);
  assert.match(code, /no browser probe \(no -BrowserProbe\): only the HTTP probes run, under the browser trap, and no page of the candidate is walked in a browser/);
  // Rollback and forward again are printed for the owner with promotion, never run.
  assert.match(code, /Write-Host "  > az containerapp ingress traffic set -g \$ResourceGroup -n \$AppName --revision-weight \$LiveRevision=100"/);
  // A failed probe deactivates the candidate (the removal step) and stops; a candidate that passes while promotion refuses
  // its data's age (the probe's exit 3, R87 and O11) stays, with no traffic, and no promotion or rollback command is printed.
  assert.match(script, /\$promotionRefused = \$LASTEXITCODE -eq 3\r?\nif \(\$LASTEXITCODE -ne 0 -and -not \$promotionRefused\) \{[\s\S]*?Disable-Candidate 'the probe failed'[\s\S]*?throw "The candidate failed its probe/);
  const answered = code.slice(code.indexOf('Step "the candidate $candidate answers as the release holds"'));
  assert.match(answered, /^Step [^\n]*\n[^\n]*\nif \(\$promotionRefused\) \{\n[^}]*Promotion refuses this release:[^}]*\}\nelseif \(\[string\]::IsNullOrEmpty\(\$LiveRevision\)\) \{[^}]*\}\nelse \{\n[^}]*--revision-weight \$candidate=100"\n\}/, "the promotion command only when the data's age allows it");
  assert.match(answered, /if \(-not \[string\]::IsNullOrEmpty\(\$LiveRevision\) -and -not \$promotionRefused\) \{\n    Write-Host "  After promotion, deactivate/, "and the rollback lines, which follow a promotion, only then");
  // A candidate that never becomes ready fails the deployment itself, before the probe: it is deactivated too, if active,
  // and the run stops (review of #963: readiness answers 503 without the law, so Azure's deadline ends the deployment); only
  // if this deployment made it (the incident of 2026-10-27).
  assert.match(code, /catch \{[\s\S]*?Disable-Candidate 'the deployment failed' -IfActive[\s\S]*?throw "The candidate revision did not deploy/);
  assert.match(code, /if \(\$IfActive\) \{\s*\$active = az containerapp revision show -g \$ResourceGroup -n \$AppName --revision \$candidate --query properties\.active -o tsv 2>\$null\s*if \(\$LASTEXITCODE -ne 0 -or \$active -ne 'true'\) \{ return \}/);
  assert.ok(code.indexOf("az deployment group create") < code.indexOf("Disable-Candidate 'the deployment failed'"), "the catch is the deployment's own");
});
