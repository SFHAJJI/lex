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
import { redirectFor } from "../scripts/permalinks.mjs";
import {
  REMOTE_PRIVACY_NOTE, accentedTitleFailures, browserFailures, clockFailures, deployedReleaseFailures, liveFilesOf, luxembourgTodayNote,
  remoteRevision, revisionFailures,
} from "../scripts/deploy-probe.mjs";
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

// The one Luxembourg work the stand-in holds, and its title as the publisher wrote it.
const WORK_KEY = "loi-2017-12-21-a1117";
const WORK = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/12/21/a1117/jo";
const EXPRESSION = `${WORK}/fr`;
const TITLE = "Loi du 21 d\u00E9cembre 2017 concernant l\u2019Institut grand-ducal.";

/** An instant as the API writes one: UTC, seven fractional digits. */
const observedAt = (milliseconds) => new Date(milliseconds).toISOString().replace("Z", "0000Z");

/**
 * A title folded as a server folds it: lowered, without its accents, its words split at anything else. `folds` false is
 * the deployed fault: the image's .NET has no language library, so decomposing did nothing, a typed accent stayed, and
 * only a decomposed accent's mark was dropped.
 */
function foldTitle(text, folds) {
  return (folds ? text.normalize("NFD") : text).replace(/\p{Mn}/gu, "").toLowerCase().split(/[^\p{L}\p{Nd}]+/u).filter(Boolean).join(" ");
}

/** What the stand-in answers `resolve`, `dossier` and `search`: the ladder's exact and prefix tiers over its one title. */
function titleAnswer(operation, parameters, { folds, titlesHeld }) {
  const stored = foldTitle(TITLE, true);
  const resolved = (text) => {
    const asked = foldTitle(text, folds);
    if (!titlesHeld || asked.length === 0 || !stored.startsWith(asked)) return null;
    return { work_identifier: WORK, expressions: [EXPRESSION], match_reason: asked === stored ? "exact_normalized_title" : "prefix" };
  };
  if (operation === "resolve") {
    const found = resolved(parameters.identifier);
    return found === null
      ? { refusal: { code: titlesHeld ? "identifier_unknown" : "retrieval_mode_unavailable" }, result: null }
      : { refusal: null, result: { value: found } };
  }
  if (operation === "dossier") {
    const titles = titlesHeld ? [{ language: "fra", expression_iri: EXPRESSION, titles: [{ title: TITLE }], short_titles: [] }] : [];
    return { refusal: null, result: { value: { titles } } };
  }
  const found = resolved(parameters.query);
  return { refusal: null, result: { value: {
    work_resolution: { outcome: !titlesHeld ? "no_titles_held" : found === null ? "no_title_match" : "one_work", work: found, candidates: null },
    hits: parameters.query === "loi" ? [{ work_key: WORK_KEY, state_sha256: "e".repeat(64), applicability_date: "2018-01-01", publisher_id: "art_1" }] : [],
  } } };
}

/**
 * A stand-in revision: `/` a redirect to `/fr/`, `/fr/` and the card as the release holds them, coverage from `corpus`
 * observed at `observedAt()`, its readiness as `ready` says, one Luxembourg work with an accented title (folded unless
 * `folds` is false; none held when `titlesHeld` is false), 404 for everything else.
 */
async function revision(change = {}) {
  const answer = {
    page: PAGE, card: CARD, corpus: CORPUS, headers: HEADERS, refusal: null, extra: new Map(), root: { status: 302, location: "/fr/", vary: "Accept-Language" },
    ready: { status: 204, cache: "no-store" }, observedAt: () => observedAt(Date.now()), folds: true, titlesHeld: true, ...change,
  };
  const server = createServer((request, response) => {
    const path = request.url.split("?")[0];
    if (request.method === "GET" && path === "/") {
      response.writeHead(answer.root.status, { ...answer.headers, ...(answer.root.location ? { location: answer.root.location } : {}), ...(answer.root.vary ? { vary: answer.root.vary } : {}) });
      response.end();
    } else if (request.method === "GET" && redirectFor(path, "", request.headers["accept-language"]) !== null) {
      // A law's address with no interface language, sent to the reader's as V3 sends it.
      response.writeHead(302, { ...answer.headers, location: redirectFor(path, "", request.headers["accept-language"]), vary: "Accept-Language" });
      response.end();
    } else if (request.method === "GET" && path === "/health/ready") {
      response.writeHead(answer.ready.status, { "cache-control": answer.ready.cache, "x-content-type-options": "nosniff" });
      response.end(answer.ready.status === 204 ? undefined : "No law is mounted.\n");
    } else if (request.method === "GET" && path === "/fr/") {
      response.writeHead(200, { "content-type": "text/html; charset=utf-8", ...answer.headers });
      response.end(answer.page);
    } else if (request.method === "GET" && path === CARD_ROUTE) {
      response.writeHead(200, { "content-type": "application/json", ...answer.headers });
      response.end(answer.card);
    } else if (request.method === "POST" && path === "/api/v3/coverage") {
      response.writeHead(200, { "content-type": "application/json" });
      const context = { freshness: { observed_at: answer.observedAt() } };
      response.end(JSON.stringify(answer.refusal === null
        ? { refusal: null, context, result: { value: { mounted: { corpus_sha256: answer.corpus } } } }
        : { refusal: { code: answer.refusal }, context, result: null }));
    } else if (request.method === "POST" && ["/api/v3/resolve", "/api/v3/dossier", "/api/v3/search"].includes(path)) {
      const chunks = [];
      request.on("data", (chunk) => chunks.push(chunk));
      request.on("end", () => {
        const { operation_id: operation, parameters } = JSON.parse(Buffer.concat(chunks).toString("utf8"));
        response.writeHead(200, { "content-type": "application/json" });
        response.end(JSON.stringify(titleAnswer(operation, parameters, answer)));
      });
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

const WEB = [["fr/index.html", PAGE], ["evaluation-card.json", CARD]];

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
    ["another page at /fr/", { page: Buffer.from("<p>someone else's page</p>") }, /GET \/fr\/ answered a page that is not the release image's own/],
    ["a page at / instead of the redirect", { root: { status: 200 } }, /GET \/ answered 200, not a redirect to \/fr\//],
    ["a redirect to English for a reader naming no language", { root: { status: 302, location: "/en/", vary: "Accept-Language" } }, /GET \/ answered 302 to \/en\/, not a redirect to \/fr\//],
    ["a redirect a cache could share", { root: { status: 302, location: "/fr/" } }, /GET \/ varies by null, not Accept-Language/],
    ["no CSP", { headers: { ...HEADERS, "content-security-policy": "default-src *" } }, /carries the CSP default-src \*/],
    ["no HSTS", { headers: Object.fromEntries(Object.entries(HEADERS).filter(([name]) => name !== "strict-transport-security")) }, /carries no HSTS/],
    ["a referrer sent", { headers: { ...HEADERS, "referrer-policy": "origin" } }, /Referrer-Policy origin/],
    ["no nosniff", { headers: Object.fromEntries(Object.entries(HEADERS).filter(([name]) => name !== "x-content-type-options")) }, /carries no nosniff/],
    ["another card", { card: Buffer.from("{}") }, /is not the release's evaluation card/],
    ["another mount", { corpus: "d".repeat(64) }, /answers from corpus d{64}, not the release's c{64}/],
    ["no mount", { refusal: "no_corpus_mounted" }, /coverage refused no_corpus_mounted/],
    ["a V2 route answering", { extra: new Map([["POST /api/ask", "a V2 answer"]]) }, /POST \/api\/ask answered 200, not 404/],
    ["a server without the law", { ready: { status: 503, cache: "no-store" } }, /GET \/health\/ready answered 503 \(19 bytes\), not 204/],
    ["a readiness a cache could keep", { ready: { status: 204, cache: "max-age=60" } }, /GET \/health\/ready may be cached \(max-age=60\)/],
    ["the synthetic preview answering", { extra: new Map([["GET /api/v3-preview/resolve", "{\"synthetic\":true}"]]) },
      /GET \/api\/v3-preview\/resolve\?family=eli&coordinate=eli%2Fsynthetic-preview answered 200, naming the preview, not the 404/],
    ["no accent folded, the deployed fault", { folds: false }, /resolve of "Loi du 21 d\u00E9cembre 2017 concernant l\u2019Institut grand-ducal\." as published answered a refusal, identifier_unknown/],
    ["a clock two minutes slow", { observedAt: () => observedAt(Date.now() - 120_000) }, /the revision's clock read .+, more than a minute from this probe's/],
    ["an answer observed at no instant", { observedAt: () => undefined }, /coverage was observed at undefined, not a UTC instant in the API's shape/],
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

test("beside its pass, the probe says the revision's date in Luxembourg and the accented title it found", async () => {
  const served = await revision();
  try {
    const notes = [];
    assert.deepEqual(await revisionFailures(served.origin, { web: WEB, card: CARD, corpusSha256: CORPUS, notes }), []);
    assert.equal(notes.length, 2, JSON.stringify(notes));
    assert.match(notes[0], /^the revision's clock read \S+: \d{4}-\d{2}-\d{2} in Luxembourg, UTC\+0[12]:00, /);
    assert.equal(notes[1], `an accented title: "${TITLE}" (${WORK}), asked as published, in capitals and decomposed`);
  } finally {
    await served.close();
  }
});

test("a revision that does not fold accents fails the title as published and in capitals, and finds it decomposed, as the deployed one did", async () => {
  const served = await revision({ folds: false });
  try {
    const failures = await accentedTitleFailures(served.origin);
    assert.deepEqual(failures.map((failure) => failure.replace(TITLE, "the title").replace(/,.*$/, "")), [
      "resolve of \"the title\" as published answered a refusal",
      "resolve of \"the title\" in capitals answered a refusal",
      "search for \"the title\" names no_title_match",
    ]);
  } finally {
    await served.close();
  }
});

test("an index that holds no titles is said, not failed", async () => {
  const served = await revision({ titlesHeld: false });
  try {
    const notes = [];
    assert.deepEqual(await accentedTitleFailures(served.origin, { notes }), []);
    assert.deepEqual(notes, ["an accented title: not checked, the revision's index holds no titles"]);
  } finally {
    await served.close();
  }
});

// The image reader's instants (plan 0.8): on the Sunday summer time ends, 22:30 UTC is still that Sunday in Luxembourg
// (summer time would make it Monday), and the evening before, 22:30 UTC is already Sunday.
test("the date the probe says is the coded rule's, on either side of a clock change", () => {
  assert.match(luxembourgTodayNote("2026-10-24T22:30:00.0000000Z"),
    /: 2026-10-25 in Luxembourg, UTC\+02:00, in the hour whose date the season's offset decides; the next clock change is at 2026-10-25T01:00:00Z,/);
  assert.match(luxembourgTodayNote("2026-10-25T22:30:00.0000000Z"),
    /: 2026-10-25 in Luxembourg, UTC\+01:00, in the hour whose date the season's offset decides; the next clock change is at 2027-03-28T01:00:00Z,/);
});

test("the revision's clock may stand a minute from the probe's, and no more", () => {
  const asked = Date.UTC(2026, 9, 15, 8, 0, 0);
  const answered = asked + 250;
  assert.deepEqual([
    clockFailures(observedAt(asked - 60_000), { asked, answered }).length,
    clockFailures(observedAt(answered + 60_000), { asked, answered }).length,
    clockFailures(observedAt(asked - 60_001), { asked, answered }).length,
    clockFailures(observedAt(answered + 60_001), { asked, answered }).length,
  ], [0, 0, 1, 1]);
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
  assert.deepEqual([...files.keys()].sort(), ["evaluation-card.json", "fr/index.html"]);
  assert.ok(files.get("fr/index.html").equals(PAGE));
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
    // The release here is a rehearsal: named a release signer, it is refused, read back alone or before a probe (review of
    // #931: the command's --signer was untested as a process).
    assert.equal(await run("--release", directory, "--public-key", good, "--signer", "lex-v3 release signer"), 1,
      "a rehearsal never passes for the release signer");
    assert.equal(await run("--release", directory, "--public-key", good, "--signer", "lex-v3 release signer", "--origin", served.origin), 1,
      "nor is a revision probed against it as the release signer's");
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
    const runs = async (apiOutput, mount, options, browser, liveRoot, extra) => {
      const { readFile: read, access } = await import("node:fs/promises");
      seen = { apiOutput, report: await read(join(mount, "build-report.json")), server: await options.startServer(), options, browser, mount, extra };
      await access(mount);
      return [["coverage, with the real mount", { failures: [] }], ["search, with the real mount", { failures: ["the page does not name the refusal"] }]];
    };
    const work = { identifier: "/lu-legilux/loi-2015-12-03-n1", date: "2015-12-11", anchor: "art_1er", beforeHistory: "2015-12-10" };
    let askedOf = null;
    const held = async (origin) => { askedOf = origin; return work; };
    const failures = await browserFailures("https://candidate.example", release, { browser: "the browser", runs, held });
    assert.equal(askedOf, "https://candidate.example", "the work is chosen from the revision's own answers");
    assert.deepEqual(seen.extra, { held: work }, "and the steps ask for it (the review of #950)");
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
