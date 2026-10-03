// The release path's custody half: corpus, indexes and manifests built from custody, twice, by two independent processes,
// and compared byte for byte (the launch contract: "one command builds corpus, indexes and manifests from custody", and
// "derivation and indexes are byte-stable across two independent executions; evidence: two-build digest comparison in the
// release pipeline").
//
// Each execution is the offline derive command (`Lex.V3.Tool derive --custody --checkpoint --out`). That it sends no
// publisher request rests on its code path: derive (V3OfflineMount.DeriveAsync) takes only the custody store and reopens
// retained custody, constructing no publisher session (the only publisher client, RoutedHttpAcquisitionSession, is built by
// acquisition alone), and every replay door refuses when any wire budget was spent. The proxy-variable trap here is a
// narrower, second check: every proxy variable points at a local
// listener that counts connections, which catches a client that honours the proxy environment. It does not see a client
// built with UseProxy = false (the publisher client is one), raw sockets or DNS, and the evidence says so
// (DERIVE_TRAP_LIMITS). The first mount is what the image then carries.

import { createHash } from "node:crypto";
import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { readdir, readFile, rm } from "node:fs/promises";
import { join, relative, sep } from "node:path";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

/** What the proxy-variable trap observes, and what it does not: said beside its count, never implied by it. */
export const DERIVE_TRAP_LIMITS =
  "counts connections from clients that honour the proxy environment variables; it does not see a client built with " +
  "UseProxy = false (the publisher client is one), raw sockets or DNS. That derive sends no publisher request rests on its " +
  "code path: derive takes only the custody store and constructs no publisher session, and every replay door refuses when " +
  "any wire budget was spent.";

/** Every file under `directory`, as sorted `{ name, sha256 }` with `/`-separated relative names. */
export async function mountDigests(directory) {
  const files = [];
  async function walk(at) {
    for (const entry of await readdir(at, { withFileTypes: true })) {
      const path = join(at, entry.name);
      if (entry.isDirectory()) await walk(path);
      else if (entry.isFile()) files.push({ name: relative(directory, path).split(sep).join("/"), sha256: sha256(await readFile(path)) });
      else throw new Error(`${path} is neither a file nor a directory; a mount holds files only`);
    }
  }
  await walk(directory);
  return files.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
}

/** The names whose digests differ between two digest lists, or that only one of them holds. */
export function digestDifferences(first, second) {
  const a = new Map(first.map((file) => [file.name, file.sha256]));
  const b = new Map(second.map((file) => [file.name, file.sha256]));
  return [...new Set([...a.keys(), ...b.keys()])].sort().filter((name) => a.get(name) !== b.get(name));
}

function runProcess(command, args, env) {
  return new Promise((resolvePromise) => {
    const child = spawn(command, args, { env, stdio: ["ignore", "pipe", "pipe"] });
    let output = "";
    child.stdout.on("data", (chunk) => { output += chunk; });
    child.stderr.on("data", (chunk) => { output += chunk; });
    child.on("close", (code) => resolvePromise({ code, output: output.slice(-4000) }));
    child.on("error", (error) => resolvePromise({ code: -1, output: String(error) }));
  });
}

/**
 * Derives the mount twice and compares it. `runner` is the command that runs the tool (`["dotnet", "<Lex.V3.Tool.dll>"]`);
 * `into` receives `derive-a` and `derive-b`. Returns the first mount's path and the evidence; throws when an execution fails,
 * connects to the proxy-variable trap (which sees only clients that honour the proxy variables: DERIVE_TRAP_LIMITS), or the two
 * mounts differ in any file.
 */
export async function deriveTwice({ runner, custody, checkpoint, custodyEncoding = "brotli", into, log = () => {} }) {
  if (!Array.isArray(runner) || runner.length === 0) throw new Error("deriveTwice needs the command that runs the derive tool");
  let connections = 0;
  const trap = createServer((socket) => { connections += 1; socket.destroy(); });
  await new Promise((done) => trap.listen(0, "127.0.0.1", done));
  const proxy = `http://127.0.0.1:${trap.address().port}`;
  const env = { ...process.env };
  for (const name of ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"]) env[name] = proxy;
  env.NO_PROXY = "";
  env.no_proxy = "";
  const executions = [];
  try {
    for (const label of ["derive-a", "derive-b"]) {
      const out = join(into, label);
      await rm(out, { recursive: true, force: true });
      log(`deriving the mount from custody (${label})`);
      const started = Date.now();
      const { code, output } = await runProcess(runner[0], [...runner.slice(1), "derive", "--custody", custody, "--checkpoint", checkpoint,
        "--out", out, "--custody-encoding", custodyEncoding], env);
      executions.push({ label, exitCode: code, seconds: Math.round((Date.now() - started) / 1000) });
      if (code !== 0) throw new Error(`${label}: the derive command exited ${code}: ${output}`);
    }
  } finally {
    await new Promise((done) => trap.close(done));
  }
  if (connections !== 0) throw new Error(`the derive command connected to the proxy-variable trap ${connections} time(s); a derivation from custody opens no session`);
  const first = await mountDigests(join(into, "derive-a"));
  const second = await mountDigests(join(into, "derive-b"));
  if (first.length === 0) throw new Error("the derive command wrote no mount file");
  const differences = digestDifferences(first, second);
  if (differences.length !== 0) throw new Error(`the two derivations differ: ${differences.join(", ")}`);
  return {
    mount: join(into, "derive-a"), files: first, executions, independentProcesses: 2,
    proxyVariableTrapConnections: connections, trapLimits: DERIVE_TRAP_LIMITS,
  };
}
