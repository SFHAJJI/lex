// The deploy kit's probe of a release's maintenance revision (`scripts/deploy-probe.mjs --maintenance`, run by
// `deploy.ps1 -Maintenance`; the specification of record's R60, R108, R115; the plan's item 10.14), against a local stand-in
// that answers as the API's maintenance switch does (`V3WebRoot.Maintenance.cs`): one that holds passes, and each way a
// maintenance revision can differ from its release fails with its own reason. The release side is a small published release,
// read back under its key.
//
// The browser trap is installed strict before any script is imported, as in no-browser-probes.test.mjs: the maintenance
// probe is HTTP only, so nothing it runs may find or start a browser, or any process at all; the probe the test runs as a
// command is the test's own doing, started by the process starter kept before the trap wraps it.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test, { beforeEach } from "node:test";

import { ARMED_NOTICE, TRAP_EXIT_CODE, installBrowserTrap, takeTouches } from "../scripts/browser-trap.mjs";

const { execFile } = createRequire(import.meta.url)("node:child_process");
installBrowserTrap({ strict: true });
process.env.LEX_BROWSER = process.execPath;

// Imported only now, so every script that imports the launcher gets the trap's.
const { MAINTENANCE_PROBES, liveFilesOf, maintenanceFailures, maintenanceReleaseFailures } = await import("../scripts/deploy-probe.mjs");
const { cspValue } = await import("../scripts/csp.mjs");
const { negotiateLanguage } = await import("../scripts/permalinks.mjs");
const { rehearsalKey, signRehearsal } = await import("../scripts/image-rehearsal.mjs");
const { writeLayout, writeTar } = await import("../scripts/image-reproducible.mjs");
const { ASSETS, imageSignatureAsset, publishRelease, releaseVersion } = await import("../scripts/release-assets.mjs");

beforeEach(() => { takeTouches(); });

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const CARD = readFileSync(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url));
const CORPUS = "c".repeat(64);
const SOURCE = { epoch: 1790799760, from: "commit", commit: "23af798d9c78602726280978edcedf07f9408e98", clean: true };
const TRAP_URL = new URL("../scripts/browser-trap.mjs", import.meta.url).href;
const PROBE_SCRIPT = fileURLToPath(new URL("../scripts/deploy-probe.mjs", import.meta.url));

// The release image's maintenance pages, one per interface language, as the build writes them (`live-maintenance-page.jsx`).
const MAINTENANCE = {
  fr: Buffer.from("<!doctype html><title>Lex est en cours de mise à jour</title><p>Legilux, EUR-Lex</p>\n"),
  en: Buffer.from("<!doctype html><title>Lex is being updated</title><p>Legilux, EUR-Lex</p>\n"),
};
const WEB = [
  ["fr/index.html", Buffer.from("<!doctype html><p>accueil</p>\n")], ["evaluation-card.json", CARD],
  ["maintenance/fr.html", MAINTENANCE.fr], ["maintenance/en.html", MAINTENANCE.en],
];

/** The headers a maintenance revision's page carries (`WritePageHeaders`, and Retry-After). */
const PAGE_HEADERS = {
  "content-security-policy": `${cspValue()}; frame-ancestors 'none'`,
  "strict-transport-security": "max-age=31536000; includeSubDomains",
  "referrer-policy": "no-referrer",
  "x-content-type-options": "nosniff",
  "cache-control": "no-store",
  "x-robots-tag": "noindex",
  "retry-after": "300",
};
const without = (name) => Object.fromEntries(Object.entries(PAGE_HEADERS).filter(([key]) => key !== name));

/**
 * A stand-in maintenance revision, answering as the API does with LEX_V3_MAINTENANCE=on: readiness 204; every page request
 * (a GET or HEAD outside /api/, /mcp and /health/) 503 with the page's headers and the maintenance page in the language the
 * path stands under, else the one Accept-Language chooses, with Vary; everything else the problem service_in_maintenance.
 * `change` breaks one thing; `seen` records each request.
 */
async function maintenanceRevision(change = {}) {
  const answer = {
    ready: 204, pageStatus: 503, headers: PAGE_HEADERS, pages: MAINTENANCE, vary: true,
    problem: { status: 503, code: "service_in_maintenance", retryAfter: "300" }, ...change,
  };
  const seen = [];
  const server = createServer((request, response) => {
    request.resume();
    const path = request.url.split("?")[0];
    seen.push(`${request.method} ${path}`);
    const read = request.method === "GET" || request.method === "HEAD";
    if (read && path === "/health/ready") {
      response.writeHead(answer.ready, { "cache-control": "no-store", "x-content-type-options": "nosniff" });
      response.end(answer.ready === 204 ? undefined : "No maintenance page is held in each interface language.\n");
    } else if (read && !["/api/", "/mcp", "/health/"].some((prefix) => path.startsWith(prefix))) {
      const named = ["fr", "en"].find((language) => path.startsWith(`/${language}/`)) ?? null;
      const language = named ?? negotiateLanguage(request.headers["accept-language"]);
      response.writeHead(answer.pageStatus, {
        "content-type": "text/html; charset=utf-8", ...answer.headers, ...(named === null && answer.vary ? { vary: "Accept-Language" } : {}),
      });
      response.end(request.method === "HEAD" ? undefined : answer.pages[language]);
    } else {
      const { status, code, retryAfter } = answer.problem;
      response.writeHead(status, { "content-type": "application/problem+json", "cache-control": "no-store", ...(retryAfter ? { "retry-after": retryAfter } : {}) });
      response.end(JSON.stringify({ schema: "lex-v3-transport-problem/1", type: `urn:lex:v3:transport:${code}`, title: code, status, code }));
    }
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  return { origin: `http://127.0.0.1:${server.address().port}`, seen, close: () => new Promise((resolve) => server.close(resolve)) };
}

test("a maintenance revision that answers as the API's switch does passes, asked its readiness, each page the probe names and the API", async () => {
  // The pages asked: both languages' homes, another page, and the root, whose language the reader's browser chooses.
  assert.deepEqual(MAINTENANCE_PROBES.map(({ path, language, acceptLanguage }) => [path, language, acceptLanguage]),
    [["/fr/", "fr", null], ["/en/", "en", null], ["/en/search.html", "en", null], ["/", "en", "en"]]);
  const served = await maintenanceRevision();
  try {
    assert.deepEqual(await maintenanceFailures(served.origin, { web: WEB }), []);
    assert.deepEqual(served.seen, ["GET /health/ready", ...MAINTENANCE_PROBES.map(({ path }) => `GET ${path}`), "POST /api/v3/coverage"]);
    assert.deepEqual(takeTouches(), [], "nothing reached the launcher or started a process");
  } finally {
    await served.close();
  }
});

test("each way a maintenance revision can differ from its release fails with its own reason", async () => {
  const cases = [
    ["a revision without its pages, never ready", { ready: 503 }, /^GET \/health\/ready answered 503 \(56 bytes\), not 204: the revision does not hold a maintenance page in each language$/],
    ["a revision that serves its pages, not in maintenance", { pageStatus: 200 }, /^GET \/fr\/ answered 200, not 503$/],
    ["the other language's page", { pages: { ...MAINTENANCE, fr: MAINTENANCE.en } }, /^GET \/fr\/ answered a body that is not the release image's own maintenance page \(maintenance\/fr\.html\), byte for byte$/],
    ["a page that is not the release's", { pages: { fr: Buffer.from("<p>V2's maintenance page</p>"), en: MAINTENANCE.en } }, /^GET \/fr\/ answered a body that is not the release image's own maintenance page/],
    ["no Retry-After", { headers: without("retry-after") }, /^GET \/fr\/ carries Retry-After null, not a number of seconds$/],
    ["a Retry-After that is a date", { headers: { ...PAGE_HEADERS, "retry-after": "Wed, 28 Oct 2026 07:28:00 GMT" } }, /^GET \/en\/ carries Retry-After Wed, 28 Oct 2026 07:28:00 GMT, not a number of seconds$/],
    ["a page a cache could keep", { headers: { ...PAGE_HEADERS, "cache-control": "max-age=60" } }, /^GET \/en\/search\.html may be cached \(max-age=60\)$/],
    ["a page a search engine could index", { headers: without("x-robots-tag") }, /^GET \/fr\/ carries X-Robots-Tag null, not noindex$/],
    ["no frame-ancestors", { headers: { ...PAGE_HEADERS, "content-security-policy": cspValue() } }, /^GET \/fr\/ carries the CSP .+, not .+; frame-ancestors 'none'$/],
    ["no HSTS", { headers: without("strict-transport-security") }, /^GET \/fr\/ carries no HSTS$/],
    ["a referrer sent", { headers: { ...PAGE_HEADERS, "referrer-policy": "origin" } }, /^GET \/fr\/ carries Referrer-Policy origin, not no-referrer$/],
    ["no nosniff", { headers: without("x-content-type-options") }, /^GET \/fr\/ carries no nosniff$/],
    ["a cookie", { headers: { ...PAGE_HEADERS, "set-cookie": "affinity=1" } }, /^GET \/fr\/ sets a cookie, which the cookies page says no page does$/],
    ["a root a cache could share between readers", { vary: false }, /^GET \/ \(Accept-Language: en\) varies by null, not Accept-Language$/],
    ["the API answering as a server that is not in maintenance", { problem: { status: 404, code: "unknown_route", retryAfter: null } },
      /^POST \/api\/v3\/coverage answered 404 application\/problem\+json \(unknown_route\), not 503 application\/problem\+json with the code service_in_maintenance: the revision is not in maintenance$/],
    ["the API's problem without Retry-After", { problem: { status: 503, code: "service_in_maintenance", retryAfter: null } }, /^POST \/api\/v3\/coverage carries Retry-After null, not a number of seconds$/],
  ];
  for (const [what, change, expected] of cases) {
    const served = await maintenanceRevision(change);
    try {
      const failures = await maintenanceFailures(served.origin, { web: WEB });
      assert.ok(failures.some((failure) => expected.test(failure)), `${what}: ${JSON.stringify(failures)}`);
    } finally {
      await served.close();
    }
  }

  // A release image built before its maintenance pages: nothing to hold the revision to, so nothing is asked and it fails.
  const served = await maintenanceRevision();
  try {
    assert.deepEqual(await maintenanceFailures(served.origin, { web: WEB.filter(([path]) => !path.startsWith("maintenance/")) }), [
      "the release image holds no maintenance page maintenance/fr.html, so the revision's pages cannot be held to it",
      "the release image holds no maintenance page maintenance/en.html, so the revision's pages cannot be held to it",
    ]);
    assert.deepEqual(served.seen, []);
  } finally {
    await served.close();
  }
  assert.deepEqual(takeTouches(), []);
});

/** A small release image: the API, and the live pages under app/v3-web/, the maintenance pages among them. */
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

test("the maintenance revision is probed only against a release that reads back, and as deploy.ps1 runs the command, under the trap, its exit says what it found", async () => {
  const root = await mkdtemp(join(tmpdir(), "lex-deploy-maintenance-"));
  const [inMaintenance, serving] = [await maintenanceRevision(), await maintenanceRevision({ pageStatus: 200 })];
  try {
    const key = rehearsalKey();
    const { archive, manifestDigest } = imageArchive();
    assert.deepEqual(new Map(liveFilesOf(archive)).get("maintenance/en.html"), MAINTENANCE.en, "the maintenance pages are read out of the release image");
    const signed = signRehearsal({ manifestDigest, reference: "lex-v3-rehearsal:rehearsal", key });
    const { directory } = await publishRelease(root, {
      version: releaseVersion(SOURCE), source: SOURCE, manifestDigest, corpusSha256: CORPUS, key,
      assets: [[ASSETS.image, archive], [ASSETS.imageSignature, imageSignatureAsset(signed)], [ASSETS.card, CARD], [ASSETS.mountReport, Buffer.from("{\"files\":[]}")]],
    });
    assert.deepEqual(await maintenanceReleaseFailures(inMaintenance.origin, directory, { publicKeyPem: key.publicKeyPem }), []);
    const unverified = await maintenanceReleaseFailures(inMaintenance.origin, directory, { publicKeyPem: rehearsalKey().publicKeyPem });
    assert.ok(unverified.length > 0 && unverified.every((failure) => failure.startsWith("the release does not read back:")), JSON.stringify(unverified));
    assert.deepEqual(takeTouches(), []);

    // The command, as deploy.ps1 runs it: the trap loaded first by --import, the browser a stand-in (node), asynchronously so
    // the stand-ins in this process answer it.
    const publicKey = join(root, "signing.pem");
    await writeFile(publicKey, key.publicKeyPem);
    const env = { ...process.env, LEX_BROWSER: process.execPath, LEX_BROWSER_TRAP: "" };
    const probe = (...args) => new Promise((resolve) => execFile(process.execPath, ["--import", TRAP_URL, PROBE_SCRIPT, ...args], { env, timeout: 60_000 },
      (error, stdout, stderr) => resolve({ code: error === null ? 0 : error.code, stdout, stderr })));
    const release = ["--release", directory, "--public-key", publicKey];
    const armed = `${ARMED_NOTICE} ${TRAP_EXIT_CODE}`;

    const passed = await probe("--maintenance", "--origin", inMaintenance.origin, ...release);
    assert.equal(passed.code, 0, passed.stdout + passed.stderr);
    assert.match(passed.stdout, /^the maintenance revision at http:\/\/127\.0\.0\.1:\d+ answers as the release holds: .*over HTTP only \(no page walked in a browser\)$/m);
    assert.ok(passed.stderr.includes(armed) && !passed.stderr.includes("was touched"), passed.stderr);

    const failed = await probe("--maintenance", "--origin", serving.origin, ...release);
    assert.equal(failed.code, 1, failed.stdout + failed.stderr);
    assert.match(failed.stderr, /^- GET \/fr\/ answered 200, not 503$/m);
    assert.match(failed.stdout, /^\d+ probe failure\(s\)$/m);

    // The release here is a rehearsal: named a release signer, it is refused before the revision is asked anything.
    assert.equal((await probe("--maintenance", "--origin", inMaintenance.origin, ...release, "--signer", "lex-v3 release signer")).code, 1);
    // A maintenance probe names the revision, and is HTTP alone: no origin, a browser, the opening's check or the promotion
    // check is a usage error, before anything is read.
    for (const extra of [[], ["--origin", inMaintenance.origin, "--browser"], ["--origin", inMaintenance.origin, "--opening"], ["--origin", inMaintenance.origin, "--promotion-check"]]) {
      const usage = await probe("--maintenance", ...release, ...extra);
      assert.equal(usage.code, 2, `${extra.join(" ")}: ${usage.stderr}`);
      assert.match(usage.stderr, /--maintenance --origin <the maintenance revision's URL>/);
    }
    assert.deepEqual(takeTouches(), []);
  } finally {
    await inMaintenance.close();
    await serving.close();
    await rm(root, { recursive: true, force: true });
  }
});
