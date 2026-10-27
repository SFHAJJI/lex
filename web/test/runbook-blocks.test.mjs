// The incident runbook's PowerShell blocks (docs/INCIDENT-RUNBOOK.md, section 3), run as an operator pastes them, under a
// made-up Azure CLI (the review of #995, F1). PowerShell does not stop when a native command fails: on 2026-10-27 a failed
// step went on to the next, and the runbook's first promotion lines would have deactivated the live revision after a
// traffic move that failed. Each block is now one `& { ... }` that stops at the first failure, waits until its target is
// running and healthy before it moves traffic, reads the traffic back, and deactivates only when the traffic reads 100 per
// cent on the target and none on the revision it leaves.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import test from "node:test";

const RUNBOOK = readFileSync(new URL("../../docs/INCIDENT-RUNBOOK.md", import.meta.url), "utf8");

/** The runbook's marked blocks, by name: the PowerShell fence that follows each `<!-- runbook:{name} -->`. */
export function runbookBlocks(text) {
  const blocks = {};
  for (const match of text.matchAll(/<!-- runbook:([a-z-]+) -->\r?\n```powershell\r?\n([\s\S]*?)\r?\n```/g)) {
    assert.ok(!Object.hasOwn(blocks, match[1]), `one block named ${match[1]}`);
    blocks[match[1]] = match[2];
  }
  return blocks;
}

const BLOCKS = runbookBlocks(RUNBOOK);
const TARGET = "ca-lex-v3--candidate";
const FROM = "ca-lex-v3--live";
const quote = (text) => `'${text.replaceAll("'", "''")}'`;

// The made-up tools: `az` holds the app's traffic as Azure does and fails where an environment variable says; Start-Sleep
// and Get-Date are the test's clock, so the wait loop runs in no time; pwsh and Invoke-WebRequest are recorded, never run.
const STUBS = `
  $global:calls = [System.Collections.Generic.List[string]]::new()
  $global:traffic = '${JSON.stringify([{ revisionName: FROM, weight: 100 }, { latestRevision: true, weight: 0, label: "candidate" }])}' | ConvertFrom-Json
  $global:states = @("$env:STATES" -split '\\|' | Where-Object { $_ })
  $global:shown = 0
  $global:slept = 0
  $global:clock = [datetime]'2026-10-27T00:00:00'
  $global:asked = 0
  function global:Get-Date { $global:clock = $global:clock.AddSeconds([int]"0$env:CLOCK_STEP"); $global:clock }
  function global:Start-Sleep { param([int] $Seconds) $global:slept += $Seconds }
  function global:pwsh { $global:calls.Add("pwsh $($args -join ' ')"); $global:LASTEXITCODE = 0 }
  function global:Invoke-WebRequest {
    $global:asked += 1
    $status = if ($global:asked -gt [int]"0$env:ANSWERED") { [int]"0$env:THEN" } else { 200 }
    [pscustomobject]@{ StatusCode = $status }
  }
  function global:az {
    $line = $args -join ' '
    $global:calls.Add("az $line")
    $global:LASTEXITCODE = 0
    if ($line -like 'containerapp revision activate*') { if ($env:ACTIVATE_FAILS) { $global:LASTEXITCODE = 1 }; return }
    if ($line -like 'containerapp revision show*') {
      if ($env:SHOW_FAILS) { $global:LASTEXITCODE = 3; return }
      $state = if ($global:states.Count -eq 0) { 'Running,Healthy' } else { $global:states[[Math]::Min($global:shown, $global:states.Count - 1)] }
      $global:shown += 1
      $running, $health = $state -split ','
      return '{"active":true,"running":"' + $running + '","health":"' + $health + '"}'
    }
    if ($line -like 'containerapp ingress traffic set*') {
      if ($env:TRAFFIC_SET_FAILS) { [Console]::Error.WriteLine('ERROR: traffic set refused: the revision is not active'); $global:LASTEXITCODE = 1; return }
      if (-not $env:TRAFFIC_SET_NO_EFFECT) {
        $weight = ($args | Where-Object { "$_" -like '*=100' } | Select-Object -First 1)
        $name = "$weight".Split('=')[0]
        $global:traffic = @([pscustomobject]@{ revisionName = $name; weight = 100 }, [pscustomobject]@{ revisionName = '${FROM}'; weight = 0 })
      }
      return
    }
    if ($line -like 'containerapp ingress traffic show*') { if ($env:TRAFFIC_SHOW_FAILS) { $global:LASTEXITCODE = 3; return }; return ($global:traffic | ConvertTo-Json -Compress -AsArray) }
    if ($line -like 'containerapp revision deactivate*') { return }
    if ($line -like 'account show*') { return 'a-subscription' }
    if ($line -like 'containerapp revision list*') { return "$env:LIVE" }
    return 'made-up'
  }`;

/** Runs the named blocks one after another, as pasted after the names block: what they said, and every call they made. */
function paste(names, env = {}) {
  const script = [
    STUBS,
    BLOCKS.names,
    `$target = ${quote(TARGET)}; $from = ${quote(FROM)}`,
    ...names.map((name) => BLOCKS[name]),
    `"SLEPT:$global:slept"`,
    `foreach ($call in $global:calls) { "CALL:$call" }`,
  ].join("\n");
  const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], {
    encoding: "utf8",
    env: { ...process.env, STATES: "", CLOCK_STEP: "1", ACTIVATE_FAILS: "", SHOW_FAILS: "", TRAFFIC_SET_FAILS: "", TRAFFIC_SET_NO_EFFECT: "", TRAFFIC_SHOW_FAILS: "", LIVE: "", ANSWERED: "1000", THEN: "429", ...env },
  });
  assert.equal(run.error, undefined, `pwsh could not run: ${run.error}`);
  const lines = run.stdout.split(/\r?\n/);
  const calls = lines.filter((line) => line.startsWith("CALL:")).map((line) => line.slice("CALL:".length));
  return {
    out: lines.filter((line) => !line.startsWith("CALL:")).join("\n"),
    calls,
    slept: Number(lines.find((line) => line.startsWith("SLEPT:"))?.slice("SLEPT:".length)),
    deactivated: calls.filter((call) => call.startsWith("az containerapp revision deactivate")),
    moved: calls.filter((call) => call.startsWith("az containerapp ingress traffic set")),
  };
}

test("the runbook's marked blocks are there, and each but the names is one block that stops at a failed step", () => {
  assert.deepEqual(Object.keys(BLOCKS).sort(), ["activate", "deactivate", "deploy", "limit-first", "move", "names"]);
  for (const [name, code] of Object.entries(BLOCKS)) {
    if (name === "names") continue;
    assert.match(code, /^& \{\n/, `${name}: one block, so a failure stops the rest of it`);
    assert.match(code, /\n\}$/, `${name}: closed at its end`);
    const destructive = code.split("\n").filter((line) => /az containerapp (revision (activate|deactivate)|ingress traffic set)\b/.test(line));
    for (const line of destructive) {
      const next = code.split("\n")[code.split("\n").indexOf(line) + 1] ?? "";
      assert.match(next, /if \(\$LASTEXITCODE -ne 0\) \{ .*return \}/, `${name}: ${line.trim()} is followed by its guard`);
    }
  }
  // The promotion's old lines, one after the other with no guard, are gone.
  assert.doesNotMatch(RUNBOOK, /traffic set [^\n]*\n\s*az containerapp revision deactivate/);
});

test("a traffic move that fails switches nothing off: the reviewer's repro of #995, pasted as written", () => {
  const run = paste(["move", "deactivate"], { TRAFFIC_SET_FAILS: "1" });
  assert.equal(run.moved.length, 1, "the move was asked");
  assert.deepEqual(run.deactivated, [], `nothing deactivated:\n${run.out}`);
  assert.match(run.out, /Moving the traffic to ca-lex-v3--candidate failed: stop here/);
  assert.match(run.out, /The traffic is not all on ca-lex-v3--candidate \(0 per cent, and 100 on ca-lex-v3--live\): stop here; nothing deactivated/);
});

test("a promotion waits until the target is running and healthy, moves the traffic, reads it back, then deactivates the one it leaves", () => {
  const run = paste(["move", "deactivate"], { STATES: "Activating,None|Running,Unhealthy|Running,Healthy" });
  assert.equal(run.slept, 30, "two waits of 15 seconds");
  const order = run.calls.map((call) => call.split(" ").slice(1, 4).join(" "));
  assert.deepEqual(order, [
    "containerapp revision show", "containerapp revision show", "containerapp revision show",
    "containerapp ingress traffic", "containerapp ingress traffic", "containerapp ingress traffic", "containerapp revision deactivate",
  ]);
  assert.match(run.moved[0], /--revision-weight ca-lex-v3--candidate=100/);
  assert.deepEqual(run.deactivated, ["az containerapp revision deactivate -g rg-lex-v3 -n ca-lex-v3 --revision ca-lex-v3--live"]);
  assert.match(run.out, /All traffic is on ca-lex-v3--candidate/);
  assert.match(run.out, /ca-lex-v3--live is deactivated/);
});

test("a target that never becomes healthy moves no traffic, and the next block switches nothing off", () => {
  const run = paste(["move", "deactivate"], { STATES: "Provisioning,None", CLOCK_STEP: "240" });
  assert.deepEqual(run.moved, [], "no traffic moved");
  assert.deepEqual(run.deactivated, []);
  assert.match(run.out, /is not running and healthy after ten minutes \(Provisioning, None\): stop here; no traffic moved/);
});

test("a move that does not take is caught by the read-back, and nothing is deactivated", () => {
  const run = paste(["move", "deactivate"], { TRAFFIC_SET_NO_EFFECT: "1" });
  assert.match(run.out, /The traffic reads 0 per cent on ca-lex-v3--candidate: stop here/);
  assert.deepEqual(run.deactivated, []);
});

test("a revision or a traffic that cannot be read stops the block that reads it", () => {
  const unread = paste(["move", "deactivate"], { SHOW_FAILS: "1" });
  assert.deepEqual(unread.moved, []);
  assert.deepEqual(unread.deactivated, []);
  assert.match(unread.out, /could not be read: stop here; no traffic moved/);
  const blind = paste(["deactivate"], { TRAFFIC_SHOW_FAILS: "1" });
  assert.deepEqual(blind.deactivated, []);
  assert.match(blind.out, /The traffic could not be read: stop here; nothing deactivated/);
});

test("a failed activation stops its block, and a rollback's three blocks run in order when each step holds", () => {
  const failed = paste(["activate"], { ACTIVATE_FAILS: "1" });
  assert.match(failed.out, /Activating ca-lex-v3--candidate failed: stop here/);
  assert.doesNotMatch(failed.out, /is activated/);
  const rollback = paste(["activate", "move", "deactivate"]);
  assert.deepEqual(rollback.calls.map((call) => call.split(" ")[3]), ["activate", "show", "traffic", "traffic", "traffic", "deactivate"]);
});

test("the deployment block names the revision serving now, and stops when it cannot", () => {
  const none = paste(["deploy"], { LIVE: "" });
  assert.match(none.out, /The revision serving now could not be read: stop here/);
  assert.ok(!none.calls.some((call) => call.startsWith("pwsh")), "deploy.ps1 is not run");
  const named = paste(["deploy"], { LIVE: FROM });
  const deploy = named.calls.find((call) => call.startsWith("pwsh "));
  assert.ok(deploy, "deploy.ps1 is run");
  for (const token of ["-File deploy/deploy.ps1", `-LiveRevision ${FROM}`, "-Public", "-SearchEngines indexed", "-Apply", "-SignerIdentity lex-v3-release"]) {
    assert.ok(deploy.includes(token), `${token} in ${deploy}`);
  }
});

test("the first half of the two-address check says whether this address is limited at all (the review of #995, F3)", () => {
  const limited = paste(["limit-first"], { ANSWERED: "70", THEN: "429" });
  assert.match(limited.out, /200: 70, 429: 80/);
  assert.match(limited.out, /This address is limited/);
  const never = paste(["limit-first"], { ANSWERED: "1000" });
  assert.match(never.out, /No 429 from this address: the limit counts no one behind this ingress\. Do not promote\./);
  // And the second half, from another machine, must print 200 within the minute.
  assert.match(RUNBOOK, /It must print 200\. A 429 there means the ingress shows every reader as one address/);
});
