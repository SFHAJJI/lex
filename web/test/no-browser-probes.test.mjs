// A release probed without a browser (`image-rehearsal.mjs --no-browser`, `deploy-probe.mjs` without `--browser`), held to
// starting none: on the driver's machine no browser may run (the owner's order of 2026-10-16, after headless Chrome runs
// from tests made 263 failed Windows sign-ins and locked the owner's account).
//
// The browser trap (`browser-trap.mjs`) is installed strict before any script is imported: the launcher's `findBrowser`
// and `launchBrowser` are traps, and so is every process start. Then the rehearsal's probe phase (`probeImage`) runs
// against a stand-in image, a local server that answers as a release image does and exposes a container's record (its
// output and its /tmp's events), and the deployment probe against the same stand-in, in this process and as `deploy.ps1`
// runs it without `-BrowserProbe`. Each must run every probe that needs no browser, say what it did not walk, and leave
// the trap untouched; with the browser asked for, each must reach the trap, which shows the trap would catch it. Every
// process this test starts has node standing in for the browser (`LEX_BROWSER`), so even an unarmed trap reaches none.

import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { readFileSync } from "node:fs";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test, { beforeEach } from "node:test";

import { ARMED_NOTICE, TRAP_EXIT_CODE, installBrowserTrap, preloaded, startsBrowser, takeTouches } from "../scripts/browser-trap.mjs";

// The test's own process starter, kept before the trap wraps every one: the deployment probe's command run as a process
// is the test's doing, not the code under test's.
const { execFile } = createRequire(import.meta.url)("node:child_process");
installBrowserTrap({ strict: true });
// A stand-in for the browser, here and in every process this test starts: were a trap not to hold, the launcher would find
// node, which refuses a browser's flags and exits, never a browser (the review of #979, F3).
process.env.LEX_BROWSER = process.execPath;

// Imported only now, so every script that imports the launcher gets the trap's.
const {
  NO_BROWSER, TRAP_HOLDS, V2_ROUTES, commandOptions, probeFailures, probeImage, recordedRequests, rehearsalKey, rehearse, signRehearsal,
} = await import("../scripts/image-rehearsal.mjs");
const { NOT_FOUND_PROBE, NOT_WALKED_NOTE, deployedReleaseFailures } = await import("../scripts/deploy-probe.mjs");
const { MCP_PROTOCOL_VERSION, REAL_MOUNT_EVENTS_VERIFIED, fixtureMountExpectations, realMountSteps } = await import("../scripts/journey.mjs");
const { cspValue } = await import("../scripts/csp.mjs");
const { CARD_ROUTE } = await import("../scripts/evaluation-card.mjs");
const { redirectFor } = await import("../scripts/permalinks.mjs");
const { ROBOTS_KEPT_OUT } = await import("../scripts/search-engines.mjs");
const { writeLayout, writeTar } = await import("../scripts/image-reproducible.mjs");
const { ASSETS, imageSignatureAsset, publishRelease, releaseVersion } = await import("../scripts/release-assets.mjs");

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

// Each test holds its own touches: one a failing test left is not the next test's.
beforeEach(() => { takeTouches(); });
const CARD = readFileSync(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url));
const PAGE = Buffer.from("<!doctype html><title>Lex V3</title><p>V3's own page</p>\n");
const NOT_FOUND_PAGE = Buffer.from("<!doctype html><title>Page introuvable - Lex</title><p>V3's own 404 page</p>\n");
const WEB = [["fr/index.html", PAGE], ["evaluation-card.json", CARD], ["not-found/fr.html", NOT_FOUND_PAGE]];
const CORPUS = "c".repeat(64);
const SOURCE = { epoch: 1790799760, from: "commit", commit: "23af798d9c78602726280978edcedf07f9408e98", clean: true };
const HEADERS = {
  "content-security-policy": `${cspValue()}; frame-ancestors 'none'`,
  "strict-transport-security": "max-age=31536000",
  "referrer-policy": "no-referrer",
  "x-content-type-options": "nosniff",
};
const TRAP_URL = new URL("../scripts/browser-trap.mjs", import.meta.url).href;
const PROBE_SCRIPT = fileURLToPath(new URL("../scripts/deploy-probe.mjs", import.meta.url));
const REHEARSAL_SCRIPT = fileURLToPath(new URL("../scripts/image-rehearsal.mjs", import.meta.url));
const DEPLOY_SCRIPT = fileURLToPath(new URL("../../deploy/deploy.ps1", import.meta.url));

/** An instant as the API writes one: UTC, seven fractional digits. */
const observedAt = (milliseconds) => new Date(milliseconds).toISOString().replace("Z", "0000Z");
const LOG = "e".repeat(64);

/**
 * What the stand-in's API answers an operation, the same through REST and MCP but for the two fields that name the request
 * and its instant: coverage from the release's corpus, the contained assistant's card for ask, an index holding no titles
 * for search, a genesis log of `events`, every permalink verifying, and no drift.
 */
function envelopeFor(operation, parameters, events) {
  const head = { request_ref: randomBytes(8).toString("hex"), context: { freshness: { observed_at: observedAt(Date.now()) } } };
  const answer = (value) => ({ verdict: "answer", ...head, refusal: null, result: { value } });
  if (operation === "coverage") return answer({ mounted: { corpus_sha256: CORPUS } });
  if (operation === "ask") {
    return { verdict: "point", ...head, refusal: null, result: { value: {
      presentation_result: "assistant_v3_unavailable", question_read: false, containment: { model_gloss: "disabled" },
      deterministic_actions: ["resolve", "as_of", "evidence_bundle"].map((id) => ({ operation_id: id, route: `/api/v3/${id}` })),
    } } };
  }
  if (operation === "search") return answer({ work_resolution: { outcome: "no_titles_held" }, hits: [] });
  if (operation === "events") {
    const last = `${LOG}:${events.length}`;
    if (parameters.after === undefined) return answer({ events, has_more: false, next_after: last });
    if (parameters.after === last) return answer({ events: [], has_more: false, next_after: last });
    return { verdict: "refuse", ...head, refusal: { code: "snapshot_unknown" }, result: null };
  }
  if (operation === "verify") return answer({ verdict: "digest_matches" });
  if (operation === "answer_drift") return answer({ invalidated_answers: [], asserts_no_drift_in_law: false });
  return answer({ operation, parameters });
}

/**
 * A stand-in for the release image running in its container: a local server answering as the image does (`/` a redirect
 * to `/fr/`, the home page as built, the 404 page, the card, robots.txt, readiness, V3's redirects at V2's law addresses,
 * the API through REST and MCP, 404 for everything else), and `start()` answering it in the journey's server shape, with
 * the record a container exposes. `records` makes it write each request on its output, with the address, and a SQLite file
 * on its /tmp; `cookie` set a cookie on the home page; `card` serve another card.
 */
async function standInImage({ records = false, cookie = false, card = CARD, events = 30 } = {}) {
  const seen = [];
  const counts = { starts: 0, closes: 0 };
  const log = Array.from({ length: events }, (_, index) => ({ seq: index + 1, permalink: `/lu-legilux/w/2024-02-01--${"a".repeat(64)}/fra#event-${index + 1}` }));
  let output = "the API's startup lines\n";
  const tmpEvents = [];
  const server = createServer((request, response) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => {
      const [path, query = ""] = request.url.split("?");
      seen.push(`${request.method} ${path}`);
      const send = (status, headers, body) => { response.writeHead(status, headers); response.end(body); };
      if (request.method === "POST" && (path.startsWith("/api/v3/") || path === "/mcp")) {
        const body = JSON.parse(Buffer.concat(chunks).toString("utf8"));
        const [operation, parameters] = path === "/mcp" ? [body.params.name, body.params.arguments ?? {}] : [path.slice("/api/v3/".length), body.parameters ?? {}];
        if (records) {
          output += `${operation} ${JSON.stringify(parameters)} from 127.0.0.1\n`;
          tmpEvents.push(["100", "/tmp/etilqs_1"]);
        }
        const envelope = envelopeFor(operation, parameters, log);
        if (path !== "/mcp") return send(200, { "content-type": "application/json" }, JSON.stringify(envelope));
        return send(200, { "content-type": "application/json", "mcp-protocol-version": MCP_PROTOCOL_VERSION },
          JSON.stringify({ jsonrpc: "2.0", id: body.id, result: { isError: false, content: [{ type: "text", text: JSON.stringify(envelope) }], structuredContent: envelope } }));
      }
      if (request.method !== "GET") return send(404, {}, "");
      if (path === "/") return send(302, { ...HEADERS, location: "/fr/", vary: "Accept-Language" }, "");
      if (path === "/fr/") return send(200, { "content-type": "text/html; charset=utf-8", ...HEADERS, ...(cookie ? { "set-cookie": "seen=1" } : {}) }, PAGE);
      if (path === CARD_ROUTE) return send(200, { "content-type": "application/json", ...HEADERS }, card);
      if (path === NOT_FOUND_PROBE) return send(404, { "content-type": "text/html; charset=utf-8", ...HEADERS }, NOT_FOUND_PAGE);
      if (path === "/robots.txt") return send(200, { "content-type": "text/plain; charset=utf-8", ...HEADERS }, ROBOTS_KEPT_OUT);
      if (path === "/health/ready") return send(204, { "cache-control": "no-store" }, "");
      const location = redirectFor(path, query === "" ? "" : `?${query}`, request.headers["accept-language"]);
      if (location !== null) return send(302, { ...HEADERS, location, vary: "Accept-Language" }, "");
      return send(404, {}, "");
    });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const origin = `http://127.0.0.1:${server.address().port}`;
  return {
    origin, seen, counts,
    async start() {
      counts.starts += 1;
      const [outputAtStart, eventsAtStart] = [output.length, tmpEvents.length];
      return {
        origin, output: () => output, outputAtStart,
        fileWatch: { stop: async () => tmpEvents.slice(eventsAtStart) },
        changedFiles: async () => [],
        close: async () => { counts.closes += 1; },
      };
    },
    close: () => new Promise((resolve) => server.close(resolve)),
  };
}

/** A mount as the probes read it: a real mount's build report (with an EU index when `europe`), or the fixture's manifest. */
async function mountOf(kind, { europe = false } = {}) {
  const directory = await mkdtemp(join(tmpdir(), "lex-no-browser-mount-"));
  const report = { corpus: { Sha256: CORPUS }, luxembourgIndex: { Sha256: "d".repeat(64) }, ...(europe ? { europeIndex: { Sha256: "f".repeat(64) } } : {}) };
  const fixture = { schema: "lex-v3-journey-mount/1", corpus_sha256: CORPUS, index_sha256: "d".repeat(64), work_key: "loi-1991-08-10-n3" };
  if (kind === "real") await writeFile(join(directory, "build-report.json"), JSON.stringify(report));
  else await writeFile(join(directory, "journey-mount.json"), JSON.stringify(fixture));
  return { directory, report, fixture };
}

test("the trap is armed: the launcher and every process start are traps, and a probe that asks for the browser stops at it", async () => {
  const { findBrowser, launchBrowser } = await import("../scripts/browser-evidence.mjs");
  assert.equal(findBrowser.lexBrowserTrap, true, "findBrowser is the trap's");
  assert.equal(launchBrowser.lexBrowserTrap, true, "launchBrowser is the trap's");
  const childProcess = createRequire(import.meta.url)("node:child_process");
  for (const name of ["spawn", "spawnSync", "execFile", "execFileSync", "exec", "execSync", "fork"]) assert.equal(childProcess[name].lexBrowserTrap, true, name);
  const { spawn } = await import("node:child_process");
  assert.equal(spawn.lexBrowserTrap, true, "and the binding every script imports");
  assert.throws(() => childProcess.spawnSync(process.execPath, ["--version"]), /the browser trap was touched: spawnSync/, "strict: no process starts at all");
  assert.equal(takeTouches().length, 1);

  // Only once both traps are seen to hold: the probe phase with the browser asked for, which must stop at the launcher.
  const image = await standInImage();
  const mount = await mountOf("real");
  try {
    await assert.rejects(
      probeImage({ startServer: () => image.start(), mountPath: mount.directory, mountKind: "real", web: WEB, card: CARD, corpusSha256: CORPUS, browser: true }),
      /the browser trap was touched: findBrowser\(\); nothing was started/);
    assert.deepEqual(takeTouches(), ["findBrowser()"], "the walk looks for a browser first, and the trap stops it there");
  } finally {
    await image.close();
    await rm(mount.directory, { recursive: true, force: true });
  }
});

test("--no-browser probes the image over HTTP, every probe that needs no browser, names each page step it did not walk, and never touches the trap", async () => {
  const image = await standInImage();
  const mount = await mountOf("real", { europe: true });
  try {
    const report = await probeImage({ startServer: () => image.start(), mountPath: mount.directory, mountKind: "real", web: WEB, card: CARD, corpusSha256: CORPUS, browser: false });
    assert.deepEqual(takeTouches(), [], "nothing reached the launcher or started a process");
    assert.deepEqual(probeFailures(report), [], "a stand-in that answers as the release holds passes");

    // Said, so no report reads as browser-probed: the walk was skipped, and each of its steps is named.
    assert.equal(report.browserProbes, NO_BROWSER);
    assert.equal(report.browserProbes, "skipped: --no-browser");
    assert.deepEqual(report.stepsNotWalked, realMountSteps(mount.report).map(([name]) => `${name}, with the real mount`));
    assert.equal(report.stepsNotWalked.length, 19, "the 13 screens, J1, J2, J5 and the three EU steps");
    assert.ok(report.stepsNotWalked.includes("eu reading, with the real mount"));

    // The API's journeys, J6 to J8, through REST and MCP; the real mount's first permalinks of its log verified.
    assert.equal(report.probes[0].step, "J6, a question that must be refused as legal advice, with the real mount");
    assert.equal(report.probes.at(-1).step, "J8, the event log polled and answer drift, with the real mount");
    assert.ok(report.probes.length > 10 && report.probes.every((one) => one.state === "api" && one.failures.length === 0));
    assert.equal(image.seen.filter((request) => request === "POST /api/v3/verify").length, REAL_MOUNT_EVENTS_VERIFIED, "of 30 events, the real mount's first 25");

    // The deployment probe's checks, V2's routes, the server's answers and the card, all on one start of the image.
    assert.deepEqual(report.servedPages.failures, []);
    assert.match(report.servedPages.notes[0], /^the revision's clock read /);
    assert.deepEqual(report.servedPages.notes.slice(1), ["an accented title: not checked, the revision's index holds no titles"]);
    assert.deepEqual(report.v2Routes, { routesAsked: V2_ROUTES.length, routeFailures: [] });
    assert.deepEqual(report.server, { failures: [] });
    assert.deepEqual({ ...report.cardRoute, sha256: undefined }, { route: CARD_ROUTE, status: 200, contentType: "application/json", sha256: undefined, isTheReleaseCard: true, failures: [] });
    for (const request of ["GET /", "GET /fr/", `GET ${NOT_FOUND_PROBE}`, `GET ${CARD_ROUTE}`, "GET /health/ready", "GET /api/v3-preview/resolve", "GET /robots.txt",
      "GET /built", "POST /api/ask", "POST /api/v3/coverage", "POST /api/v3/ask", "POST /mcp", "POST /api/v3/events", "POST /api/v3/answer_drift"]) {
      assert.ok(image.seen.includes(request), `${request} was asked`);
    }
    assert.deepEqual(image.counts, { starts: 1, closes: 1 }, "one start of the image for every probe, closed once");

    // Privacy as far as it shows: the container recorded nothing once it answered; no answer set a cookie; every request
    // the probes sent went to the image, and reached it.
    assert.deepEqual(report.privacy.failures, []);
    assert.equal(report.privacy.requests, image.seen.length);
    assert.ok(report.privacy.textsSent > 0);
    assert.deepEqual([report.privacy.outputBytes, report.privacy.fileEvents, report.privacy.cookiesSet, report.privacy.otherOrigins], [0, 0, 0, []]);
  } finally {
    await image.close();
    await rm(mount.directory, { recursive: true, force: true });
  }
});

test("on the fixture mount, --no-browser names the fixture's steps and verifies every event of its log", async () => {
  const image = await standInImage();
  const mount = await mountOf("fixture");
  try {
    const report = await probeImage({ startServer: () => image.start(), mountPath: mount.directory, mountKind: "fixture", web: WEB, card: CARD, corpusSha256: CORPUS, browser: false });
    assert.deepEqual(takeTouches(), []);
    assert.deepEqual(probeFailures(report), []);
    assert.deepEqual(report.stepsNotWalked, fixtureMountExpectations(mount.fixture).map(([name]) => `${name}, with the fixture mount`));
    assert.ok(report.probes.every((one) => one.step.endsWith(", with the fixture mount")));
    assert.equal(image.seen.filter((request) => request === "POST /api/v3/verify").length, 30, "every permalink of the fixture's log");
  } finally {
    await image.close();
    await rm(mount.directory, { recursive: true, force: true });
  }
});

test("without a browser, what the image records, a cookie it sets and a card that is not the release's still fail", async () => {
  const image = await standInImage({ records: true, cookie: true, card: Buffer.from("{}") });
  const mount = await mountOf("real");
  try {
    const report = await probeImage({ startServer: () => image.start(), mountPath: mount.directory, mountKind: "real", web: WEB, card: CARD, corpusSha256: CORPUS, browser: false });
    assert.deepEqual(takeTouches(), []);
    const failures = probeFailures(report);
    for (const expected of [
      /^privacy: the API process recorded the query text "assemblée générale"$/,
      /^privacy: the API process recorded an address$/,
      /^privacy: the API process wrote output during the run: /,
      /^privacy: the API process touched files while serving the run: \/tmp\/etilqs_1$/,
      /^privacy: GET \/fr\/ answered with a cookie$/,
      /^the image does not serve the card at \/evaluation-card\.json: /,
      /^the served pages: GET \/evaluation-card\.json is not the release's evaluation card, byte for byte$/,
    ]) {
      assert.ok(failures.some((failure) => expected.test(failure)), `${expected}: ${JSON.stringify(failures)}`);
    }
    assert.equal(report.browserProbes, NO_BROWSER, "a failing report says the walk was skipped too");
  } finally {
    await image.close();
    await rm(mount.directory, { recursive: true, force: true });
  }
});

test("the probes' requests are recorded, another origin among them, and the global fetch is put back", async () => {
  const [one, other] = [await standInImage(), await standInImage()];
  const original = globalThis.fetch;
  try {
    const { value, record } = await recordedRequests(one.origin, async () => {
      await (await fetch(`${one.origin}/fr/`)).arrayBuffer();
      await (await fetch(`${other.origin}/api/v3/search`, { method: "POST", body: JSON.stringify({ operation_id: "search", parameters: { query: "congé parental", language: "fra" } }) })).arrayBuffer();
      return "done";
    });
    assert.equal(value, "done");
    assert.deepEqual(record, { requests: 2, otherOrigins: [other.origin], cookies: [], texts: ["congé parental"] }, "texts under eight characters are not kept");
    assert.equal(globalThis.fetch, original);
    await assert.rejects(recordedRequests(one.origin, async () => { throw new Error("a probe that throws"); }), /a probe that throws/);
    assert.equal(globalThis.fetch, original, "put back when a probe throws too");
  } finally {
    await one.close();
    await other.close();
  }
});

/** A small release image: the API, and the live pages under app/v3-web/. */
function imageArchive() {
  const file = (path, bytes) => ({ path, type: "file", mode: 0o644, uid: 0, gid: 0, bytes });
  const layer = writeTar([file("app/Lex.V3.Api.dll", Buffer.from("MZ the api")), ...WEB.map(([path, bytes]) => file(`app/v3-web/${path}`, bytes))], { mtime: SOURCE.epoch });
  const config = Buffer.from(JSON.stringify({ architecture: "amd64", os: "linux", rootfs: { type: "layers", diff_ids: [`sha256:${sha256(layer)}`] } }));
  const manifest = Buffer.from(JSON.stringify({
    schemaVersion: 2,
    config: { mediaType: "application/vnd.oci.image.config.v1+json", size: config.length, digest: `sha256:${sha256(config)}` },
    layers: [{ mediaType: "application/vnd.oci.image.layer.v1.tar", size: layer.length, digest: `sha256:${sha256(layer)}` }],
  }));
  const manifestDigest = `sha256:${sha256(manifest)}`;
  const index = { schemaVersion: 2, manifests: [{ mediaType: "application/vnd.oci.image.manifest.v1+json", size: manifest.length, digest: manifestDigest }] };
  const blobs = new Map([layer, config, manifest].map((bytes) => [`sha256:${sha256(bytes)}`, bytes]));
  return { archive: writeLayout({ index, blobs }, { mtime: SOURCE.epoch }), manifestDigest };
}

test("the deployment probe without --browser runs its HTTP probes and never reaches the trap, in this process and as deploy.ps1 runs it without -BrowserProbe", async () => {
  const image = await standInImage();
  const root = await mkdtemp(join(tmpdir(), "lex-no-browser-release-"));
  try {
    const key = rehearsalKey();
    const { archive, manifestDigest } = imageArchive();
    const signed = signRehearsal({ manifestDigest, reference: "lex-v3-rehearsal:rehearsal", key });
    const { directory } = await publishRelease(root, {
      version: releaseVersion(SOURCE), source: SOURCE, manifestDigest, corpusSha256: CORPUS, key,
      assets: [[ASSETS.image, archive], [ASSETS.imageSignature, imageSignatureAsset(signed)], [ASSETS.card, CARD], [ASSETS.mountReport, Buffer.from("{\"files\":[]}")]],
    });
    // What the command runs without --browser, in this process, the trap strict.
    assert.deepEqual(await deployedReleaseFailures(image.origin, directory, { publicKeyPem: key.publicKeyPem }), []);
    assert.deepEqual(takeTouches(), []);

    // The command as a process, the trap loaded before it by `--import` alone (no LEX_BROWSER_TRAP: loaded first, it arms
    // itself; the review of #979, F1 d), as deploy.ps1 runs it without -BrowserProbe; the browser a stand-in (node), so an
    // unarmed trap could reach nothing; asynchronously, so the stand-in revision in this process answers it.
    const publicKey = join(root, "signing.pem");
    await writeFile(publicKey, key.publicKeyPem);
    const env = { ...process.env, LEX_BROWSER: process.execPath, LEX_BROWSER_TRAP: "" };
    const node = (args) => new Promise((resolve) => execFile(process.execPath, args, { env },
      (error, stdout, stderr) => resolve({ code: error === null ? 0 : error.code, stdout, stderr })));
    const armed = `${ARMED_NOTICE} ${TRAP_EXIT_CODE}`;
    // First, that the trap a process loads this way holds: the launcher it then imports is the trap's (the review's F3: the
    // control below once passed on an unarmed trap).
    const launcherUrl = JSON.stringify(new URL("../scripts/browser-evidence.mjs", import.meta.url).href);
    const holds = await node(["--import", TRAP_URL, "--input-type=module", "--eval",
      `const { findBrowser, launchBrowser } = await import(${launcherUrl}); console.log(findBrowser.lexBrowserTrap === true && launchBrowser.lexBrowserTrap === true);`]);
    assert.deepEqual([holds.code, holds.stdout, holds.stderr.includes(armed)], [0, "true\n", true], holds.stderr);

    const probe = (...args) => node(["--import", TRAP_URL, PROBE_SCRIPT, "--release", directory, "--public-key", publicKey, "--origin", image.origin, ...args]);
    const probed = await probe();
    assert.equal(probed.code, 0, probed.stderr);
    assert.ok(probed.stderr.includes(armed), "the trap says it is armed");
    assert.ok(probed.stdout.includes(NOT_WALKED_NOTE), "it says no page was walked in a browser");
    assert.match(probed.stdout, /answers as the release holds, over HTTP only \(no page walked in a browser\)/);
    assert.ok(!probed.stderr.includes("was touched"), probed.stderr);
    // The control, on a trap seen armed: with --browser the same command reaches the launcher, and the trap fails it there.
    const walked = await probe("--browser");
    assert.ok(walked.stderr.includes(armed), walked.stderr);
    assert.equal(walked.code, TRAP_EXIT_CODE, walked.stderr);
    assert.match(walked.stderr, /the browser trap was touched: findBrowser\(\); nothing was started, and the run ends with 97/);
    // Asked for by LEX_BROWSER_TRAP=exit instead, the trap arms as its module loads; and a caller that catches its error, or
    // ends with its own code, still ends with the trap's.
    const caught = await new Promise((resolve) => execFile(process.execPath, ["--input-type=module", "--eval",
      `await import(${JSON.stringify(TRAP_URL)});
       const { findBrowser } = await import(${launcherUrl});
       try { await findBrowser(); } catch { console.log("caught"); }
       process.exit(0);`],
    { env: { ...env, LEX_BROWSER_TRAP: "exit" } }, (error, stdout, stderr) => resolve({ code: error === null ? 0 : error.code, stdout, armed: stderr.includes(armed) })));
    assert.deepEqual(caught, { code: TRAP_EXIT_CODE, stdout: "caught\n", armed: true });
  } finally {
    await image.close();
    await rm(root, { recursive: true, force: true });
  }
});

test("a run's trap refuses every browser the launcher knows and every page walker, and none of a release's own processes", async () => {
  const { browserCandidates } = await import("../scripts/browser-evidence.mjs");
  for (const platform of ["win32", "linux", "darwin"]) {
    for (const path of browserCandidates(platform)) {
      assert.ok(startsBrowser([path, ["--headless=new", "about:blank"]]), path);
      assert.ok(startsBrowser(["cmd", ["/c", path]]), `${path}, as an argument`);
    }
  }
  assert.ok(startsBrowser(["start msedge https://example.invalid"]), "a shell's command line");
  assert.ok(startsBrowser([process.execPath, ["scripts/journey.mjs", "--api", "api", "--mount", "mount"]]), "the journey, run by node");
  assert.ok(startsBrowser([process.execPath, ["C:\\lex\\web\\scripts\\browser-evidence.mjs"]]), "the evidence run");
  for (const own of [
    ["dotnet", ["publish", "src/Lex.V3.Api/Lex.V3.Api.csproj", "-p:LexImageWebRoot=C:\\Temp\\lex-image-rehearsal-1\\first\\v3-web", "-t:PublishContainer"]],
    ["git", ["-C", "C:\\lex-v3\\checkout", "rev-parse", "HEAD"]],
    ["wsl", ["-d", "Ubuntu-24.04", "-u", "root", "-e", "sh", "-c", "exec env -i 'ASPNETCORE_URLS=http://0.0.0.0:5000' chroot --userspec=1654:1654 \"$R\" 'dotnet' '/app/Lex.V3.Api.dll'"]],
    ["taskkill /PID 12 /T /F"],
  ]) {
    assert.equal(startsBrowser(own), false, own.join(" "));
  }
  assert.equal(startsBrowser(["git", ["status"]], { strict: true }), true, "strict: every start");

  // A run whose own script walks pages is refused as it loads, before the script runs (its API directory does not exist
  // either, so even run it would stop before a browser).
  const refused = await new Promise((resolve) => execFile(process.execPath, ["--import", TRAP_URL, fileURLToPath(new URL("../scripts/journey.mjs", import.meta.url)), "--api", join(tmpdir(), "no-such-api"), "--mount", "none"],
    { env: { ...process.env, LEX_BROWSER: process.execPath, LEX_BROWSER_TRAP: "" } }, (error, stdout, stderr) => resolve({ code: error === null ? 0 : error.code, stderr })));
  assert.equal(refused.code, TRAP_EXIT_CODE, refused.stderr);
  assert.match(refused.stderr, /the browser trap was touched: the script [^\n]*journey\.mjs; nothing was started/);

  // Loaded first, by `--import` in either spelling, the trap is a run's; imported by a script, it is the script's to install.
  assert.equal(preloaded(["--import", "./scripts/browser-trap.mjs"]), true);
  assert.equal(preloaded([`--import=${TRAP_URL}`]), true);
  assert.equal(preloaded(["--import", "./scripts/other-trap.mjs", "--expose-gc"]), false);
  assert.equal(preloaded([]), false);
});

test("the release command names --no-browser beside --no-probe", async () => {
  const usage = await new Promise((resolve) => execFile(process.execPath, [REHEARSAL_SCRIPT], (error, stdout, stderr) => resolve({ code: error?.code ?? 0, stderr })));
  assert.equal(usage.code, 2);
  assert.match(usage.stderr, /\[--no-probe \| --no-browser\]/);
});

test("the release command's --no-browser is browser: false, and the command then installs the trap before it runs anything", async () => {
  // The arguments as the command maps them (the review of #979, F1 c: a command that set browser: true passed every test).
  const base = { keep: false, probe: true, reproduce: true, platformCard: false };
  assert.deepEqual(commandOptions(["--mount", "m"]), { ...base, browser: true });
  assert.deepEqual(commandOptions(["--mount", "m", "--no-browser"]), { ...base, browser: false });
  assert.deepEqual(commandOptions(["--custody", "c", "--checkpoint", "p", "--tool", "t", "--no-browser", "--no-reproduce"]), { ...base, browser: false, reproduce: false });
  assert.deepEqual(commandOptions(["--mount", "m", "--no-probe", "--keep", "--platform-card"]), { ...base, browser: true, probe: false, keep: true, platformCard: true });

  // As a process, with no trap loaded and none asked for by the environment: --no-browser installs it and sees it hold, before
  // the rehearsal reads its mount (a mount that does not exist, so nothing is built either way).
  const missing = join(tmpdir(), `lex-no-such-mount-${process.pid}`);
  const command = (...args) => new Promise((resolve) => execFile(process.execPath, [REHEARSAL_SCRIPT, "--mount", missing, ...args],
    { env: { ...process.env, LEX_BROWSER: process.execPath, LEX_BROWSER_TRAP: "" } }, (error, stdout, stderr) => resolve({ code: error === null ? 0 : error.code, stderr })));
  const without = await command("--no-browser");
  assert.equal(without.code, 1, without.stderr);
  const [armedAt, holdsAt, mountAt] = [without.stderr.indexOf(`${ARMED_NOTICE} ${TRAP_EXIT_CODE}`), without.stderr.indexOf(`- ${TRAP_HOLDS}`), without.stderr.indexOf("build-report.json")];
  assert.ok(armedAt >= 0 && holdsAt > armedAt && mountAt > holdsAt, `armed, then seen to hold, then the mount is read: ${without.stderr}`);
  // Without --no-browser the command installs no trap (a release with its browser walk, where a browser may run).
  const walking = await command();
  assert.equal(walking.code, 1, walking.stderr);
  assert.ok(!walking.stderr.includes(ARMED_NOTICE) && walking.stderr.includes("build-report.json"), walking.stderr);
});

test("deploy.ps1 walks no page unless -BrowserProbe asks: a plan run reads the release back under the trap and plans no browser", async () => {
  const root = await mkdtemp(join(tmpdir(), "lex-no-browser-plan-"));
  try {
    const key = rehearsalKey();
    const { archive, manifestDigest } = imageArchive();
    const signed = signRehearsal({ manifestDigest, reference: "lex-v3-rehearsal:rehearsal", key });
    const { directory } = await publishRelease(join(root, "releases"), {
      version: releaseVersion(SOURCE), source: SOURCE, manifestDigest, corpusSha256: CORPUS, key,
      assets: [[ASSETS.image, archive], [ASSETS.imageSignature, imageSignatureAsset(signed)], [ASSETS.card, CARD], [ASSETS.mountReport, Buffer.from("{\"files\":[]}")]],
    });
    const publicKey = join(root, "signing.pem");
    await writeFile(publicKey, key.publicKeyPem);
    // A plan run runs no Azure or registry command (each is only shown without -Apply); stand-ins for both, first on the
    // path, would say so if one ran.
    const stubs = join(root, "stubs");
    const { chmod, mkdir } = await import("node:fs/promises");
    await mkdir(stubs);
    for (const tool of ["az", "oras"]) {
      await writeFile(join(stubs, `${tool}.cmd`), `@echo STUB ${tool} RAN\r\n@exit /b 99\r\n`);
      await writeFile(join(stubs, tool), `#!/bin/sh\necho "STUB ${tool} RAN"\nexit 99\n`);
      await chmod(join(stubs, tool), 0o755);
    }
    const pathKey = Object.keys(process.env).find((name) => name.toUpperCase() === "PATH") ?? "PATH";
    const env = { ...process.env, [pathKey]: `${stubs}${process.platform === "win32" ? ";" : ":"}${process.env[pathKey]}`, LEX_BROWSER: process.execPath, LEX_BROWSER_TRAP: "" };
    const plan = (...extra) => new Promise((resolve) => execFile("pwsh", ["-NoProfile", "-NonInteractive", "-File", DEPLOY_SCRIPT,
      "-Subscription", "00000000-0000-0000-0000-000000000000", "-ResourceGroup", "rg-plan", "-EnvironmentId", "/subscriptions/plan/environment",
      "-Registry", "crplan.azurecr.io", "-IdentityResourceId", "/subscriptions/plan/identity", "-SigningPublicKey", publicKey, "-Release", directory,
      "-AllowedCidr", "203.0.113.7/32", ...extra], { env }, (error, stdout, stderr) => resolve({ code: error === null ? 0 : error.code, stdout, stderr })));
    const probeLine = (stdout) => stdout.split("\n").find((line) => line.includes("web/scripts/deploy-probe.mjs --origin")) ?? "";
    const armed = `${ARMED_NOTICE} ${TRAP_EXIT_CODE}`;

    // By default: the release read back under the trap (it says it is armed), the candidate's probe planned under it, with
    // no --browser, and said to walk no page.
    for (const [what, extra] of [["by default", []], ["with -SkipBrowserProbe, kept and changing nothing", ["-SkipBrowserProbe"]]]) {
      const run = await plan(...extra);
      assert.equal(run.code, 0, `${what}: ${run.stdout}${run.stderr}`);
      assert.ok(!run.stdout.includes("STUB"), `${what}: a plan run ran no Azure or registry command`);
      assert.ok(run.stdout.includes("reads back under the signing identity's key, as a rehearsal"), `${what}: ${run.stdout}`);
      assert.ok(run.stderr.includes(armed), `${what}: the read-back ran under the trap: ${run.stderr}`);
      assert.match(probeLine(run.stdout), /> node --import file:\/\/\/?\S*\/web\/scripts\/browser-trap\.mjs web\/scripts\/deploy-probe\.mjs --origin /, what);
      assert.ok(!/--browser\b/.test(probeLine(run.stdout)), `${what}: no --browser planned`);
      assert.match(run.stdout, /\(no browser probe: only the HTTP probes would run, under the browser trap, and no page of the candidate would be walked in a browser/);
    }
    // Asked for, on a machine where a browser may run: planned with --browser and without the trap (a plan run starts no
    // browser: the probe is only shown).
    const walking = await plan("-BrowserProbe");
    assert.equal(walking.code, 0, walking.stdout + walking.stderr);
    assert.match(probeLine(walking.stdout), /> node web\/scripts\/deploy-probe\.mjs --origin .* --browser --promotion-check\s*$/);
    assert.ok(!walking.stderr.includes(armed) && !walking.stdout.includes("no browser probe"));
    // Both at once say opposite things, and nothing is read or planned.
    const both = await plan("-BrowserProbe", "-SkipBrowserProbe");
    assert.notEqual(both.code, 0);
    // PowerShell colours an error and wraps it at the console's width, each continued line after "|": read without either.
    const said = `${both.stdout}${both.stderr}`.replace(/\x1B\[[0-9;]*m/g, "").replace(/\s*\n\s*\|\s*/g, " ").replace(/\s+/g, " ");
    assert.match(said, /-BrowserProbe asks for the browser probes and -SkipBrowserProbe skips them: name one\./);
    assert.ok(!both.stdout.includes("reads back"));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("the probes and the rehearsal take no default for the browser: one left out stops them before anything starts", async () => {
  const image = await standInImage();
  const mount = await mountOf("real");
  try {
    const probing = { startServer: () => image.start(), mountPath: mount.directory, mountKind: "real", web: WEB, card: CARD, corpusSha256: CORPUS };
    await assert.rejects(probeImage(probing), /probeImage needs browser: true \(the pages walked in a browser\) or false \(--no-browser\), and was given undefined/);
    await assert.rejects(probeImage({ ...probing, browser: "false" }), /probeImage needs browser: .* and was given false/);
    assert.deepEqual(image.counts, { starts: 0, closes: 0 }, "the image was never started");
    await assert.rejects(rehearse({ mount: mount.directory }), /the rehearsal needs browser: true .* and was given undefined/);
    await assert.rejects(rehearse({ mount: mount.directory, browser: 0 }), /the rehearsal needs browser: .* and was given 0/);
    assert.deepEqual(takeTouches(), []);
  } finally {
    await image.close();
    await rm(mount.directory, { recursive: true, force: true });
  }
});
