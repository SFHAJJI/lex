// `deploy/backup-release.ps1` (R111) run whole under pwsh, with the Azure CLI and node made up as PowerShell functions (a
// function comes before any command on the path, so nothing reaches Azure): the made-up CLI keeps a storage account's blobs
// in a temporary directory, refuses an upload over an existing blob as `--overwrite false` does, and lists a custody
// container from what the test gives it. Each rule the script states is held: it plans without -Apply and touches nothing;
// it copies nothing from a release that does not read back, nor into an account without versioning and soft delete unless
// -EnableProtection turns them on; it copies every asset, the manifest and its signature with their digests and never over an
// existing blob; it verifies every object by SHA-256 after downloading it, and the custody pointers by listing them again;
// and it never prints the account key.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { after } from "node:test";
import { fileURLToPath } from "node:url";

const SCRIPT = fileURLToPath(new URL("../../deploy/backup-release.ps1", import.meta.url));
const quote = (text) => `'${text.replaceAll("'", "''")}'`;
const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const KEY = "made-up-storage-key-never-printed";
const VERSION = "v3-20261027T100000Z-0123456789ab";

// A signed release as the script reads it once the release is read back (the read-back is the made-up node's).
const root = mkdtempSync(join(tmpdir(), "lex-backup-release-"));
after(() => rmSync(root, { recursive: true, force: true }));
const releaseDirectory = join(root, VERSION);
mkdirSync(releaseDirectory);
const ASSETS = [["evaluation-card.json", "card"], ["lex-v3-image.oci.tar", "image bytes"], ["mount-report.json", "report"]];
for (const [name, text] of ASSETS) writeFileSync(join(releaseDirectory, name), text);
writeFileSync(join(releaseDirectory, "release-manifest.json"), JSON.stringify({
  schema: "lex-v3-release/1", version: VERSION, rehearsal: false, signer: "lex-v3-release",
  assets: ASSETS.map(([name, text]) => ({ name, size: Buffer.byteLength(text), sha256: sha256(Buffer.from(text)) })),
}));
writeFileSync(join(releaseDirectory, "release-manifest.sig.json"), JSON.stringify({ signature: "made up", signer: "lex-v3-release", rehearsal: false }));
const publicKey = join(root, "signing.pem");
writeFileSync(publicKey, "made up: the release read-back is the made-up node's\n");

const STUBS = `
  $global:calls = [System.Collections.Generic.List[string]]::new()
  $global:store = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
  $value = { param($all, $name) $at = [array]::IndexOf($all, $name); if ($at -ge 0) { $all[$at + 1] } }
  function global:node { $global:calls.Add('node ' + ($args -join ' ')); $global:LASTEXITCODE = if ($env:READBACK_FAILS) { 1 } else { 0 } }
  function global:az {
    $line = $args -join ' '
    $global:calls.Add("az $line")
    $global:LASTEXITCODE = 0
    if ($line -like 'storage account keys list*') { return '${KEY}' }
    if ($line -like 'storage account blob-service-properties show*') { return $env:PROTECTION }
    if ($line -like 'storage account blob-service-properties update*') { return }
    if ($line -like 'storage container create*') { return }
    if ($line -like 'storage blob show*') {
      $name = & $value $args '--name'
      if ($env:EXISTING -and $name -like "*$env:EXISTING" -and -not $global:store.ContainsKey($name)) { return '{"size":4,"sha256":"' + ('0' * 64) + '"}' }
      if (-not $global:store.ContainsKey($name)) { $global:LASTEXITCODE = 3; return }
      $entry = $global:store[$name]
      return (@{ size = $entry.size; sha256 = $entry.sha256 } | ConvertTo-Json -Compress)
    }
    if ($line -like 'storage blob upload*') {
      $name = & $value $args '--name'
      if ($global:store.ContainsKey($name)) { $global:LASTEXITCODE = 1; return }
      $target = Join-Path $env:STORE_DIR ([guid]::NewGuid().ToString('N'))
      Copy-Item -LiteralPath (& $value $args '--file') -Destination $target
      $meta = "$($args | Where-Object { "$_".StartsWith('sha256=') } | Select-Object -First 1)".Substring(7)
      $global:store[$name] = @{ path = $target; size = (Get-Item -LiteralPath $target).Length; sha256 = $meta }
      return
    }
    if ($line -like 'storage blob download*') {
      $name = & $value $args '--name'
      $file = & $value $args '--file'
      Copy-Item -LiteralPath $global:store[$name].path -Destination $file
      if ($env:DAMAGED -and $name -like "*$env:DAMAGED") { [System.IO.File]::AppendAllText($file, 'x') }
      return
    }
    if ($line -like 'storage blob list*') { return $env:CUSTODY }
  }`;

/**
 * Runs the script once or several times in one session (the made-up account keeps its blobs between them), each with its own
 * parameters and environment: what each said and whether it threw, and every call made.
 */
function backup(runs, env = {}) {
  const store = mkdtempSync(join(tmpdir(), "lex-backup-store-"));
  try {
    // The host's own free disk is not this test's: the margin kept free while verifying is none here.
    const common = ["-Release", quote(releaseDirectory), "-SigningPublicKey", quote(publicKey), "-KeepFreeBytes", "0"];
    const body = runs.map(({ parameters = [], env: own = {} }, index) => `
      ${Object.entries(own).map(([name, text]) => `$env:${name} = ${quote(text)}`).join("; ")}
      $before = $global:calls.Count
      try { & ${quote(SCRIPT)} ${[...common, ...parameters].join(" ")} *>&1 | ForEach-Object { "OUT${index}:$_" }; "RESULT${index}:returned" } catch { "RESULT${index}:threw $($_.Exception.Message)" }
      ${Object.keys(own).map((name) => `Remove-Item Env:${name} -ErrorAction SilentlyContinue`).join("; ")}`).join("\n");
    const script = `${STUBS}\n${body}\nforeach ($call in $global:calls) { "CALL:$call" }`;
    const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], {
      encoding: "utf8", timeout: 120_000,
      env: { ...process.env, STORE_DIR: store, PROTECTION: '{"versioning":true,"blobs":true,"containers":true}', READBACK_FAILS: "", EXISTING: "", DAMAGED: "", CUSTODY: "[]", ...env },
    });
    assert.equal(run.error, undefined, `pwsh could not run: ${run.error}`);
    const lines = run.stdout.split(/\r?\n/);
    return {
      raw: run.stdout + run.stderr,
      results: runs.map((_, index) => lines.find((line) => line.startsWith(`RESULT${index}:`))?.slice(`RESULT${index}:`.length) ?? `no result: ${run.stdout}${run.stderr}`),
      outs: runs.map((_, index) => lines.filter((line) => line.startsWith(`OUT${index}:`)).map((line) => line.slice(`OUT${index}:`.length)).join("\n")),
      calls: lines.filter((line) => line.startsWith("CALL:")).map((line) => line.slice("CALL:".length)),
    };
  } finally {
    rmSync(store, { recursive: true, force: true });
  }
}

const uploads = (calls) => calls.filter((call) => call.startsWith("az storage blob upload "));
const OBJECTS = [...ASSETS.map(([name]) => name), "release-manifest.json", "release-manifest.sig.json"];

test("without -Apply the backup reads the release back and plans: it asks nothing of Azure", () => {
  const run = backup([{}]);
  assert.equal(run.results[0], "returned", run.raw);
  assert.match(run.outs[0], new RegExp(`plan: copy 5 objects of ${VERSION} to stlexv3soufien/releases/${VERSION}/`));
  assert.ok(run.calls[0].startsWith("node --import file:///") && run.calls[0].includes("--signer lex-v3-release"), run.calls[0]);
  assert.deepEqual(run.calls.filter((call) => call.startsWith("az ")), [], "no Azure call, not even the key");
});

test("a release that does not read back is copied nowhere", () => {
  const run = backup([{ parameters: ["-Apply"] }], { READBACK_FAILS: "1" });
  assert.equal(run.results[0], "threw The release does not verify under the signing identity's key; nothing is copied.");
  assert.deepEqual(run.calls.filter((call) => call.startsWith("az ")), []);
});

test("an account without versioning and soft delete is refused before anything is copied, unless -EnableProtection turns them on", () => {
  const unprotected = '{"versioning":false,"blobs":true,"containers":false}';
  const refused = backup([{ parameters: ["-Apply"] }], { PROTECTION: unprotected });
  assert.match(refused.results[0], /^threw stlexv3soufien keeps no versions or soft-deleted copies \(versioning False, blob soft delete True, container soft delete False\); R111 asks for versioned storage\./);
  assert.deepEqual(uploads(refused.calls), []);
  const turnedOn = backup([{ parameters: ["-Apply", "-EnableProtection"] }], { PROTECTION: unprotected });
  assert.equal(turnedOn.results[0], "returned", turnedOn.raw);
  assert.ok(turnedOn.calls.includes("az storage account blob-service-properties update -n stlexv3soufien -g rg-lex-v3 --enable-versioning true --enable-delete-retention true --delete-retention-days 365 --enable-container-delete-retention true --container-delete-retention-days 365 -o none"));
  assert.equal(uploads(turnedOn.calls).length, 5);
});

test("every asset, the manifest and its signature are copied with their digests, never over a blob, and each is verified by SHA-256", () => {
  const run = backup([{ parameters: ["-Apply"] }, { parameters: ["-Apply"] }, { parameters: ["-Verify"] }]);
  assert.deepEqual(run.results, ["returned", "returned", "returned"], run.raw);
  const copied = uploads(run.calls);
  assert.equal(copied.length, 5, "the second run copies nothing again");
  for (const name of OBJECTS) {
    const call = copied.find((upload) => upload.includes(`--name ${VERSION}/${name} `));
    assert.ok(call, `${name} is copied`);
    const bytes = name.startsWith("release-manifest") ? null : Buffer.from(ASSETS.find(([asset]) => asset === name)[1]);
    if (bytes) assert.ok(call.includes(`sha256=${sha256(bytes)}`), call);
    assert.match(call, / --tier Cool .* --overwrite false --validate-content /);
  }
  assert.match(run.outs[1], new RegExp(`kept: ${VERSION}/lex-v3-image\\.oci\\.tar is already copied`));
  for (const out of run.outs) assert.match(out, new RegExp(`verified: 5 objects of ${VERSION} in stlexv3soufien/releases by SHA-256`));
  assert.equal(run.calls.filter((call) => call.startsWith("az storage blob download ")).length, 15, "each object downloaded once per verification");
  assert.ok(!run.raw.includes(KEY), "the account key is never printed");
  assert.ok(!run.calls.some((call) => call.includes(KEY)), "nor put on a command line");
});

test("a blob that exists with another content stops the copy, and a damaged copy fails its verification", () => {
  const existing = backup([{ parameters: ["-Apply"] }], { EXISTING: "mount-report.json" });
  assert.match(existing.results[0], new RegExp(`^threw ${VERSION}/mount-report\\.json exists in releases with another content \\(size 4, sha256 0{64}\\); a copy is never overwritten\\. Stop here\\.$`));
  assert.ok(!uploads(existing.calls).some((call) => call.includes("mount-report.json")));
  const damaged = backup([{ parameters: ["-Apply"] }], { DAMAGED: "evaluation-card.json" });
  assert.match(damaged.results[0], new RegExp(`^threw ${VERSION}/evaluation-card\\.json downloaded is sha256 [0-9a-f]{64}, not the ${sha256(Buffer.from("card"))} the release holds: the copy is damaged\\.$`));
});

test("the custody pointers name every custody object under the prefixes, and the verification lists them again", () => {
  const custody = (md5) => JSON.stringify([
    { name: "run15/aa.br", size: 10, md5: "bWQ1LWE=", version: "2026-10-12T00:00:00.0000000Z" },
    { name: "run15/bb.br", size: 20, md5, version: "2026-10-12T00:00:01.0000000Z" },
  ]);
  const run = backup([
    { parameters: ["-Apply", "-CustodyPrefix", "run15/"], env: { CUSTODY: custody("bWQ1LWI=") } },
    { parameters: ["-Verify"], env: { CUSTODY: custody("bWQ1LWI=") } },
    { parameters: ["-Verify"], env: { CUSTODY: custody("Y2hhbmdlZA==") } },
  ]);
  assert.equal(run.results[0], "returned", run.raw);
  assert.match(run.outs[0], new RegExp(`2 custody objects named, ${VERSION}/custody-pointers\\.json sha256 [0-9a-f]{64}`));
  assert.match(run.outs[0], /verified: 5 objects of .* by SHA-256, and 2 custody pointers/);
  assert.ok(uploads(run.calls).some((call) => call.includes(`--name ${VERSION}/custody-pointers.json `)));
  assert.equal(run.results[1], "returned", run.raw);
  assert.equal(run.results[2], "threw The custody object run15/bb.br is not the one the pointers name (size 20, Content-MD5 bWQ1LWI=, version 2026-10-12T00:00:01.0000000Z).");
});
