// The deployment kit's zero-traffic probe (`scripts/deploy-probe.mjs`), against a local stand-in for a deployed
// revision: one that answers as the release holds passes, and each way a revision can differ from its release fails
// with its own reason. The release side is a small published release, read back under its key.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { execFile } from "node:child_process";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import { cspValue } from "../scripts/csp.mjs";
import { CARD_ROUTE } from "../scripts/evaluation-card.mjs";
import { REMOTE_PRIVACY_NOTE, browserFailures, deployedReleaseFailures, liveFilesOf, remoteRevision, revisionFailures } from "../scripts/deploy-probe.mjs";
import { rehearsalKey, signRehearsal } from "../scripts/image-rehearsal.mjs";
import { writeLayout, writeTar } from "../scripts/image-reproducible.mjs";
import { ASSETS, imageSignatureAsset, publishRelease, releaseVersion } from "../scripts/release-assets.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const CARD = readFileSync(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url));
const PAGE = Buffer.from("<!doctype html><title>Lex V3</title><p>V3's own page</p>\n");
const CORPUS = "c".repeat(64);
const SOURCE = { epoch: 1790799760, from: "commit", commit: "23af798d9c78602726280978edcedf07f9408e98", clean: true };
const HEADERS = {
  "content-security-policy": `${cspValue()}; frame-ancestors 'none'`,
  "strict-transport-security": "max-age=31536000",
  "referrer-policy": "no-referrer",
  "x-content-type-options": "nosniff",
};

/** A stand-in revision: `/` and the card as the release holds them, coverage from `corpus`, 404 for everything else. */
async function revision(change = {}) {
  const answer = { page: PAGE, card: CARD, corpus: CORPUS, headers: HEADERS, refusal: null, extra: new Map(), ...change };
  const server = createServer((request, response) => {
    const path = request.url.split("?")[0];
    if (request.method === "GET" && path === "/") {
      response.writeHead(200, { "content-type": "text/html; charset=utf-8", ...answer.headers });
      response.end(answer.page);
    } else if (request.method === "GET" && path === CARD_ROUTE) {
      response.writeHead(200, { "content-type": "application/json", ...answer.headers });
      response.end(answer.card);
    } else if (request.method === "POST" && path === "/api/v3/coverage") {
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify(answer.refusal === null
        ? { refusal: null, result: { value: { mounted: { corpus_sha256: answer.corpus } } } }
        : { refusal: { code: answer.refusal }, result: null }));
    } else if (answer.extra.has(`${request.method} ${path}`)) {
      response.writeHead(200, { "content-type": "text/plain" });
      response.end(answer.extra.get(`${request.method} ${path}`));
    } else {
      response.writeHead(404);
      response.end();
    }
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  return { origin: `http://127.0.0.1:${server.address().port}`, close: () => new Promise((resolve) => server.close(resolve)) };
}

const WEB = [["index.html", PAGE], ["evaluation-card.json", CARD]];

test("a revision that answers as its release holds passes the probe", async () => {
  const served = await revision();
  try {
    assert.deepEqual(await revisionFailures(served.origin, { web: WEB, card: CARD, corpusSha256: CORPUS }), []);
  } finally {
    await served.close();
  }
});

test("each way a revision can differ from its release fails with its own reason", async () => {
  const cases = [
    ["another page at /", { page: Buffer.from("<p>someone else's page</p>") }, /GET \/ answered a page that is not the release image's own/],
    ["no CSP", { headers: { ...HEADERS, "content-security-policy": "default-src *" } }, /carries the CSP default-src \*/],
    ["no HSTS", { headers: Object.fromEntries(Object.entries(HEADERS).filter(([name]) => name !== "strict-transport-security")) }, /carries no HSTS/],
    ["a referrer sent", { headers: { ...HEADERS, "referrer-policy": "origin" } }, /Referrer-Policy origin/],
    ["no nosniff", { headers: Object.fromEntries(Object.entries(HEADERS).filter(([name]) => name !== "x-content-type-options")) }, /carries no nosniff/],
    ["another card", { card: Buffer.from("{}") }, /is not the release's evaluation card/],
    ["another mount", { corpus: "d".repeat(64) }, /answers from corpus d{64}, not the release's c{64}/],
    ["no mount", { refusal: "no_corpus_mounted" }, /coverage refused no_corpus_mounted/],
    ["a V2 route answering", { extra: new Map([["POST /api/ask", "a V2 answer"]]) }, /POST \/api\/ask answered 200, not 404/],
  ];
  for (const [what, change, expected] of cases) {
    const served = await revision(change);
    try {
      const failures = await revisionFailures(served.origin, { web: WEB, card: CARD, corpusSha256: CORPUS });
      assert.ok(failures.some((failure) => expected.test(failure)), `${what}: ${JSON.stringify(failures)}`);
    } finally {
      await served.close();
    }
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

test("the live pages are read out of the release image", () => {
  const files = new Map(liveFilesOf(imageArchive().archive));
  assert.deepEqual([...files.keys()].sort(), ["evaluation-card.json", "index.html"]);
  assert.ok(files.get("index.html").equals(PAGE));
});

test("a revision is probed only against a release that reads back under the signing identity's key", async () => {
  const root = await mkdtemp(join(tmpdir(), "lex-deploy-probe-"));
  const served = await revision();
  try {
    const key = rehearsalKey();
    const { archive, manifestDigest } = imageArchive();
    const signed = signRehearsal({ manifestDigest, reference: "lex-v3-rehearsal:rehearsal", key });
    const { directory } = await publishRelease(root, {
      version: releaseVersion(SOURCE), source: SOURCE, manifestDigest, corpusSha256: CORPUS, key,
      assets: [[ASSETS.image, archive], [ASSETS.imageSignature, imageSignatureAsset(signed)], [ASSETS.card, CARD], [ASSETS.mountReport, Buffer.from("{\"files\":[]}")]],
    });
    assert.deepEqual(await deployedReleaseFailures(served.origin, directory, { publicKeyPem: key.publicKeyPem }), []);

    const failures = await deployedReleaseFailures(served.origin, directory, { publicKeyPem: rehearsalKey().publicKeyPem });
    assert.ok(failures.length > 0 && failures.every((failure) => failure.startsWith("the release does not read back:")),
      `another key: the revision is not probed against an unverified release: ${JSON.stringify(failures)}`);

    // The command deploy.ps1 runs, as a process: its exit code is what the script acts on, so a guard that ran nothing
    // and exited 0 would deploy an unverified release (the kit's first plan run found exactly that).
    const script = fileURLToPath(new URL("../scripts/deploy-probe.mjs", import.meta.url));
    const good = join(root, "signing.pem");
    const other = join(root, "other.pem");
    await writeFile(good, key.publicKeyPem);
    await writeFile(other, rehearsalKey().publicKeyPem);
    // Asynchronously, so the stand-in revision in this process can answer the child's requests.
    const run = (...args) => new Promise((resolve) => execFile(process.execPath, [script, ...args], (error) => resolve(error === null ? 0 : error.code)));
    assert.equal(await run("--release", directory, "--public-key", good), 0, "the release reads back under its key");
    assert.equal(await run("--release", directory, "--public-key", other), 1, "under another key it does not, and the command says so by its exit");
    assert.equal(await run("--release", directory, "--public-key", good, "--origin", served.origin), 0, "the revision answers as the release holds");
    assert.equal(await run(), 2, "usage");
  } finally {
    await served.close();
    await rm(root, { recursive: true, force: true });
  }
});

test("the browser probes run the journey's real-mount steps against the revision, through a remote stand-in for its process", async () => {
  const release = await mkdtemp(join(tmpdir(), "lex-deploy-browser-release-"));
  try {
    const report = Buffer.from(JSON.stringify({ corpus: { Sha256: CORPUS }, luxembourgIndex: { Sha256: "d".repeat(64) } }));
    await writeFile(join(release, ASSETS.mountReport), report);
    let seen = null;
    const runs = async (apiOutput, mount, options, browser) => {
      const { readFile: read, access } = await import("node:fs/promises");
      seen = { apiOutput, report: await read(join(mount, "build-report.json")), server: await options.startServer(), options, browser, mount };
      await access(mount);
      return [["coverage, with the real mount", { failures: [] }], ["search, with the real mount", { failures: ["the page does not name the refusal"] }]];
    };
    const failures = await browserFailures("https://candidate.example", release, { browser: "the browser", runs });
    assert.deepEqual(failures, ["search, with the real mount: the page does not name the refusal"]);
    assert.equal(seen.apiOutput, null, "no local API is started");
    assert.ok(seen.report.equals(report), "the release's mount report stands as the build report the steps read");
    assert.equal(seen.server.origin, "https://candidate.example");
    assert.equal(seen.options.servedByApi, true, "the revision serves its own pages, so the security headers are held");
    assert.equal(seen.browser, "the browser");
    const { existsSync } = await import("node:fs");
    assert.ok(!existsSync(seen.mount), "the temporary mount report is removed");
  } finally {
    await rm(release, { recursive: true, force: true });
  }

  // The stand-in exposes no process: its privacy record is empty, which proves nothing, and the note says so.
  const server = remoteRevision("https://candidate.example");
  assert.deepEqual([server.output(), await server.fileWatch.stop(), await server.changedFiles()], ["", [], []]);
  assert.match(REMOTE_PRIVACY_NOTE, /privacy is not observed here/);
});
