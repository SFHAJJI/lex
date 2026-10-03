// The release path's custody half (derive-twice.mjs): two independent derivations from custody, compared byte for byte, with
// every proxy variable pointing at a trap. A stand-in tool plays the derive command: it writes the mount files it is told to,
// or differs on its second run, or reaches for the network.

import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { deriveTwice, digestDifferences, mountDigests } from "../scripts/derive-twice.mjs";

const STAND_IN = String.raw`
const { mkdirSync, writeFileSync, existsSync } = require("node:fs");
const { join } = require("node:path");
const args = process.argv.slice(2);
const at = (name) => args[args.indexOf(name) + 1];
if (args[0] !== "derive") process.exit(9);
const out = at("--out");
mkdirSync(join(out, "generations"), { recursive: true });
const mode = process.env.STAND_IN_MODE ?? "same";
const second = out.endsWith("derive-b");
writeFileSync(join(out, "lex-corpus-6.json"), mode === "differ" && second ? "corpus two" : "corpus one");
writeFileSync(join(out, "generations", "kept.txt"), "kept");
if (mode === "network") {
  const proxy = new URL(process.env.HTTPS_PROXY);
  const socket = require("node:net").connect(Number(proxy.port), proxy.hostname, () => { socket.destroy(); process.exit(0); });
  socket.on("error", () => process.exit(0));
} else process.exit(0);
`;

async function standIn() {
  const root = await mkdtemp(join(tmpdir(), "lex-derive-twice-"));
  const script = join(root, "stand-in.cjs");
  await writeFile(script, STAND_IN, "utf8");
  return { root, runner: [process.execPath, script] };
}

test("two derivations that agree file for file give the first mount and the evidence", async () => {
  const { root, runner } = await standIn();
  try {
    const result = await deriveTwice({ runner, custody: "custody", checkpoint: "inputs.json", into: root });
    assert.equal(result.independentProcesses, 2);
    assert.equal(result.proxyVariableTrapConnections, 0);
    assert.match(result.trapLimits, /UseProxy = false/, "the evidence says what the trap does not see");
    assert.deepEqual(result.files.map((file) => file.name), ["generations/kept.txt", "lex-corpus-6.json"]);
    assert.deepEqual(result.executions.map((execution) => execution.exitCode), [0, 0]);
    assert.equal(result.mount, join(root, "derive-a"));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("two derivations that differ in any file are refused, naming the file", async () => {
  const { root, runner } = await standIn();
  process.env.STAND_IN_MODE = "differ";
  try {
    await assert.rejects(deriveTwice({ runner, custody: "c", checkpoint: "i", into: root }), /differ: lex-corpus-6\.json/);
  } finally {
    delete process.env.STAND_IN_MODE;
    await rm(root, { recursive: true, force: true });
  }
});

test("a derivation whose client honours the proxy environment and reaches for the network is refused, even when it exits zero", async () => {
  const { root, runner } = await standIn();
  process.env.STAND_IN_MODE = "network";
  try {
    await assert.rejects(deriveTwice({ runner, custody: "c", checkpoint: "i", into: root }), /connected to the proxy-variable trap/);
  } finally {
    delete process.env.STAND_IN_MODE;
    await rm(root, { recursive: true, force: true });
  }
});

test("the derive tool is bound to the release source only when its CI artifact names the checkout's commit", async () => {
  const { toolBinding } = await import("../scripts/image-rehearsal.mjs");
  const root = await mkdtemp(join(tmpdir(), "lex-tool-binding-"));
  try {
    const { mkdir } = await import("node:fs/promises");
    await mkdir(join(root, "runtime"));
    const tool = join(root, "runtime", "Lex.V3.Tool.dll");
    await writeFile(tool, "tool bytes");
    const unstamped = await toolBinding(tool, "a".repeat(40));
    assert.equal(unstamped.bound, false);
    assert.match(unstamped.reason, /names the commit it was built from/);
    await writeFile(join(root, "source-head.txt"), "a".repeat(40) + "\n");
    const bound = await toolBinding(tool, "a".repeat(40));
    assert.equal(bound.bound, true);
    assert.match(bound.sha256, /^[0-9a-f]{64}$/);
    const other = await toolBinding(tool, "b".repeat(40));
    assert.equal(other.bound, false);
    assert.match(other.reason, /built from a{40}, and the checkout is b{40}/);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("the release from custody refuses a derive tool its CI artifact does not bind to the checkout", async () => {
  const { rehearseFromCustody } = await import("../scripts/image-rehearsal.mjs");
  const root = await mkdtemp(join(tmpdir(), "lex-tool-refusal-"));
  try {
    const { mkdir } = await import("node:fs/promises");
    await mkdir(join(root, "runtime"));
    const tool = join(root, "runtime", "Lex.V3.Tool.dll");
    await writeFile(tool, "tool bytes");
    const release = () => rehearseFromCustody({ tool, custody: join(root, "custody"), checkpoint: join(root, "mount-inputs.json") });
    // No stamp beside runtime/: nothing names the commit the tool was built from. Refused before any derive runs.
    await assert.rejects(release(), /the derive tool is not bound to the source the image is built from: no .*source-head\.txt names the commit/);
    // A stamp naming another commit than this checkout's is refused the same way.
    await writeFile(join(root, "source-head.txt"), "0".repeat(40) + "\n");
    await assert.rejects(release(), /the derive tool is not bound to the source the image is built from: it was built from 0{40}, and the checkout is [0-9a-f]{40}/);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("digest lists are compared by name: a file only one side holds is a difference", async () => {
  assert.deepEqual(digestDifferences([{ name: "a", sha256: "1" }], [{ name: "a", sha256: "1" }, { name: "b", sha256: "2" }]), ["b"]);
  assert.deepEqual(digestDifferences([{ name: "a", sha256: "1" }], [{ name: "a", sha256: "2" }]), ["a"]);
  const { root } = await standIn();
  try {
    assert.deepEqual((await mountDigests(root)).map((file) => file.name), ["stand-in.cjs"]);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
