// The deployment kit's static rules (`deploy/`), over the committed files, so they hold in CI where no Azure CLI or
// Bicep is installed (`deploy/validate.ps1` holds main.json to main.bicep's build offline). The kit is the owner's
// go-live as one command; these rules are what make it credential-free: no secret in the template or the script, the
// image pulled with a managed identity and named by digest, one server, and a new revision arriving with no traffic.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const template = JSON.parse(readFileSync(new URL("../../deploy/main.json", import.meta.url), "utf8"));
const bicep = readFileSync(new URL("../../deploy/main.bicep", import.meta.url), "utf8");
const script = readFileSync(new URL("../../deploy/deploy.ps1", import.meta.url), "utf8");
// The script's code alone: its help block and comment lines describe what it does not do, in words these rules ban.
const code = script.replace(/<#[\s\S]*?#>/g, "").split("\n").filter((line) => !line.trim().startsWith("#")).join("\n");
const app = (Array.isArray(template.resources) ? template.resources : Object.values(template.resources))
  .find((resource) => resource.type === "Microsoft.App/containerApps");

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
    [["Liveness", "/", 8080], ["Readiness", "/evaluation-card.json", 8080]]);
  assert.equal(app.properties.configuration.ingress.targetPort, 8080);
  assert.equal(app.properties.configuration.ingress.allowInsecure, false);
});

test("a new revision arrives as the candidate with no traffic while a live revision keeps it all", () => {
  const { activeRevisionsMode, ingress } = app.properties.configuration;
  assert.equal(activeRevisionsMode, "Multiple", "every revision is kept, so the live one serves while the candidate is probed");
  assert.match(ingress.traffic, /createObject\('revisionName', parameters\('liveRevision'\), 'weight', 100\), createObject\('latestRevision', true\(\), 'weight', 0, 'label', 'candidate'\)/);
  // On a first deployment there is no live revision to carry the traffic, so ingress admits only the owner's probe.
  assert.match(ingress.ipSecurityRestrictions, /if\(and\(variables\('firstDeployment'\), not\(empty\(parameters\('probeSourceCidr'\)\)\)\)/);
  assert.match(ingress.ipSecurityRestrictions, /'action', 'Allow'/);
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
  // Promotion is printed for the owner, never run: the traffic and access-restriction commands appear only inside Write-Host.
  const promotion = script.split("\n").filter((line) => line.includes("ingress traffic set") || line.includes("access-restriction remove"));
  assert.ok(promotion.length >= 2 && promotion.every((line) => line.trim().startsWith("Write-Host")), `promotion runs: ${promotion.join(" | ")}`);
  // Without -Apply nothing runs: every Azure or registry command is inside Run, which only shows it unless -Apply.
  assert.match(script, /if \(\$Apply -or \$Remove\) \{\s*& \$command/);
});

test("the script deploys only a release that verifies, and only by the image's digest", () => {
  const deploying = code.slice(code.indexOf("Step \"verify the release"));
  const verify = deploying.indexOf("deploy-probe.mjs') --release");
  const firstAzure = deploying.search(/Run "az /);
  assert.ok(verify > 0 && verify < firstAzure, "the release is read back under the signing identity's key before any Azure command");
  assert.match(script, /\$image = "\$Registry\/\$Repository@\$digest"/);
  assert.match(script, /\$digest -notmatch '\^sha256:\[0-9a-f\]\{64\}\$'/);
  assert.match(script, /"image=\$image"/);
  // A failed probe deactivates the candidate (the removal step) and stops.
  assert.match(script, /if \(\$LASTEXITCODE -ne 0\) \{[\s\S]*?revision deactivate[\s\S]*?throw "The candidate failed its probe/);
});
