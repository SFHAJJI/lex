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
import { ROBOTS_KEPT_OUT } from "../scripts/search-engines.mjs";
import {
  NOT_FOUND_PROBE, REMOTE_PRIVACY_NOTE, accentedTitleFailures, browserFailures, clockFailures, deployedReleaseFailures, homePageFailures,
  legalPageFailures, liveFilesOf, luxembourgTodayNote, remoteRevision, renderedHomePage, revisionFailures,
} from "../scripts/deploy-probe.mjs";
import { LEGAL_PAGE_PATHS, openingFailures } from "../scripts/legal-pages.mjs";
import { rehearsalKey, signRehearsal } from "../scripts/image-rehearsal.mjs";
import { writeLayout, writeTar } from "../scripts/image-reproducible.mjs";
import { ASSETS, imageSignatureAsset, publishRelease, releaseVersion } from "../scripts/release-assets.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const CARD = readFileSync(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url));
const PAGE = Buffer.from("<!doctype html><title>Lex V3</title><p>V3's own page</p>\n");
const NOT_FOUND_PAGE = Buffer.from("<!doctype html><title>Page introuvable - Lex</title><p>V3's own 404 page</p>\n");
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
function titleAnswer(operation, parameters, { folds, titlesHeld, reference }) {
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
  // The exact-reference phase (4.2) reads "Loi du 21 décembre 2017 …" as an act by type and date before any title: when
  // `reference` names a work key, the search answers as the phase does, opening that act (or, with null, opening nothing).
  if (reference !== undefined && parameters.query !== "loi") {
    return { refusal: null, result: { value: {
      reference: { phase: "exact_reference", outcome: reference === null ? "not_opened" : "opens", recognised: { act: { work: reference } },
        opens: reference === null ? null : { address: `/lu-legilux/${reference}` } },
      lanes_run: false, hits: [],
    } } };
  }
  // As the API answers the ladder's searches: the phase's member is there, with nothing recognised (V3CorpusMount.cs).
  return { refusal: null, result: { value: {
    reference: { phase: "exact_reference", outcome: "no_reference", recognised: null, opens: null },
    work_resolution: { outcome: !titlesHeld ? "no_titles_held" : found === null ? "no_title_match" : "one_work", work: found, candidates: null },
    hits: parameters.query === "loi" ? [{ work_key: WORK_KEY, state_sha256: "e".repeat(64), applicability_date: "2018-01-01", publisher_id: "art_1" }] : [],
  } } };
}

/**
 * A stand-in revision: `/` a redirect to `/fr/`, `/fr/`, the card and robots.txt as the release holds them, coverage from `corpus`
 * observed at `observedAt()`, its readiness as `ready` says, one Luxembourg work with an accented title (folded unless
 * `folds` is false; none held when `titlesHeld` is false), 404 for everything else.
 */
async function revision(change = {}) {
  const answer = {
    page: PAGE, card: CARD, corpus: CORPUS, headers: HEADERS, refusal: null, extra: new Map(), root: { status: 302, location: "/fr/", vary: "Accept-Language" },
    ready: { status: 204, cache: "no-store" }, observedAt: () => observedAt(Date.now()), folds: true, titlesHeld: true,
    notFound: { status: 404, body: NOT_FOUND_PAGE }, legal: new Map(), ...change,
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
    } else if (request.method === "GET" && path === "/robots.txt") {
      // The release's own robots.txt, kept out of search engines until O27 (R67).
      response.writeHead(200, { "content-type": "text/plain; charset=utf-8", ...answer.headers });
      response.end(ROBOTS_KEPT_OUT);
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
    } else if (request.method === "GET" && path === NOT_FOUND_PROBE) {
      // The release's 404 page, as both servers answer an address nothing claims (R60).
      response.writeHead(answer.notFound.status, { "content-type": "text/html; charset=utf-8", ...answer.headers });
      response.end(answer.notFound.body);
    } else if (request.method === "GET" && answer.legal.has(path)) {
      // A legal page (the plan's item 7.1): its bytes, with the page's headers and any the case adds (a cookie).
      const { status = 200, body, headers = {} } = answer.legal.get(path);
      response.writeHead(status, { "content-type": "text/html; charset=utf-8", ...answer.headers, ...headers });
      response.end(body);
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

const WEB = [["fr/index.html", PAGE], ["evaluation-card.json", CARD], ["not-found/fr.html", NOT_FOUND_PAGE]];

test("a revision that answers as its release holds passes the probe", async () => {
  const served = await revision();
  try {
    assert.deepEqual(await revisionFailures(served.origin, { web: WEB, card: CARD, corpusSha256: CORPUS }), []);
  } finally {
    await served.close();
  }
});

// The home page as built, and the release's renderer as a stand-in: it renders that page with the coverage line and the
// facts it was rendered from, and today in its date field, from the coverage answer it is handed, as `ssr/fr.js` does
// (`live-home-page.jsx`).
const HOME_HEAD = '<!doctype html>\n<html lang="fr"><head><meta charSet="utf-8"/><title>Le droit - Lex</title></head>';
const HOME_SCRIPT = '<script src="/fr/client-live-home.js" defer=""></script>';
const HOME_FOOTER = '<footer class="site-footer"><p data-not-advice="">Lex montre le texte de la loi.</p><p>Les textes consolidés n’ont aucun effet juridique.</p></footer>';
const BUILT_HOME = Buffer.from(`${HOME_HEAD}<body><main><h1>Le droit</h1><div id="live-home-root"><form role="search"></form></div>${HOME_SCRIPT}</main>${HOME_FOOTER}</body></html>\n`);
const HOME_RENDERER = Buffer.from(`globalThis.lexSsr = { render(kind, envelopeJson, path) {
  const envelope = JSON.parse(envelopeJson);
  const today = envelope.context.freshness.observed_at.slice(0, 10);
  const works = envelope.result.value.mounted.corpus_sha256.length;
  return ${JSON.stringify(`${HOME_HEAD}<body><main><h1>Le droit</h1><div id="live-home-root"><form role="search"></form><p data-coverage-line="counted">Ce site détient `)}
    + works + ' actes</p><input data-search-date="" value="' + today + '"/></div><script type="application/json" id="lex-home-facts">{"today":"' + today
    + '","coverage":{"state":"counted","works":' + works + ',"states":' + works + ',"articles":' + works + ',"builtOn":"2026-10-01"}}</script>'
    + ${JSON.stringify(`${HOME_SCRIPT}</main>${HOME_FOOTER}</body></html>\n`)};
} };`);

/** The home page the stand-in renderer renders from the stand-in revision's coverage answer, today. */
function renderedHome(today = new Date().toISOString().slice(0, 10)) {
  return Buffer.from(renderedHomePage(HOME_RENDERER, { context: { freshness: {} }, result: { value: { mounted: { corpus_sha256: CORPUS } } } }, today));
}

test("a revision that renders its home page from its coverage answer serves the release's own home page, and nothing else passes for it (review of #971)", async () => {
  const rendered = renderedHome();
  const text = rendered.toString("utf8");
  const web = [["fr/index.html", BUILT_HOME], ["ssr/fr.js", HOME_RENDERER], ["evaluation-card.json", CARD], ["not-found/fr.html", NOT_FOUND_PAGE]];
  const coverage = { context: { freshness: { observed_at: "2001-01-01T00:00:00.0000000Z" } }, result: { value: { mounted: { corpus_sha256: CORPUS } } } };
  const options = { renderer: HOME_RENDERER, coverage };
  assert.deepEqual(homePageFailures(rendered, BUILT_HOME, options), [], "rendered from the answer, on the facts' day");
  assert.deepEqual(homePageFailures(BUILT_HOME, BUILT_HOME, options), [], "as built");
  const NOT_OWN = /^GET \/fr\/ answered a page that is not the release image's own home page/;
  for (const [what, page, reason, change = {}] of [
    // The four pages the review of #971 found the probe passed.
    ["the footer's R50 line swapped for the old advice line", text.replace('<p data-not-advice="">Lex montre le texte de la loi.</p>', "<p>Documentation, et non consultation juridique.</p>"), /outside its root and its facts it is not the built page/],
    ["the heading and the whole root replaced, with empty facts", text.replace(/<h1>[\s\S]*<\/main>/, `<h1>Autre</h1><div id="live-home-root"><p>anything</p></div><script type="application/json" id="lex-home-facts">{}</script>${HOME_SCRIPT}</main>`), /its facts are not a home page's facts/],
    ["a count typed into the coverage line", text.replace(/Ce site détient \d+ actes/, "Ce site détient 1402 actes"), /not what the release's renderer renders from this revision's coverage answer/],
    ["facts that say other counts than the root", text.replace('"works":64', '"works":1402'), /not what the release's renderer renders/],
    // And what the probe cannot hold without the release's renderer or its own answer: it fails closed.
    ["a rendered page with no renderer in the release", text, /holds no server renderer/, { renderer: null }],
    ["a rendered page with no coverage answer", text, /no coverage answer to render it again from/, { coverage: null }],
    ["another day's facts than its root was rendered on", text.replace(/"today":"[0-9-]+"/, '"today":"2001-01-01"'), /not what the release's renderer renders/],
    ["no facts block", text.replace(/<script type="application\/json" id="lex-home-facts">[^<]*<\/script>/, ""), /carries no root followed by the facts/],
    ["two roots", text.replace("<main>", `<main>${'<div id="live-home-root"></div>'}`), /carries no root followed by the facts/],
  ]) {
    const failures = homePageFailures(Buffer.from(page), BUILT_HOME, { ...options, ...change });
    assert.ok(failures.length > 0 && failures.every((failure) => NOT_OWN.test(failure)) && failures.some((failure) => reason.test(failure)), `${what}: ${JSON.stringify(failures)}`);
  }

  // Through the probe itself: a revision serving the rendered page passes, one serving any of the four does not.
  const served = await revision({ page: rendered });
  try {
    assert.deepEqual(await revisionFailures(served.origin, { web, card: CARD, corpusSha256: CORPUS }), []);
  } finally {
    await served.close();
  }
  for (const page of [text.replace("<h1>Le droit</h1>", "<h1>Autre</h1>"), text.replace(/Ce site détient \d+ actes/, "Ce site détient 1402 actes")]) {
    const tampered = await revision({ page: Buffer.from(page) });
    try {
      const failures = await revisionFailures(tampered.origin, { web, card: CARD, corpusSha256: CORPUS });
      assert.ok(failures.some((failure) => NOT_OWN.test(failure)), JSON.stringify(failures));
    } finally {
      await tampered.close();
    }
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
    ["the platform's problem where the 404 page belongs", { notFound: { status: 404, body: "{\"code\":\"unknown_route\"}" } },
      /GET \/fr\/no-such-page answered 404 with a body that is not the release image's own 404 page, not the 404 page/],
    ["a page where nothing is", { notFound: { status: 200, body: NOT_FOUND_PAGE } }, /GET \/fr\/no-such-page answered 200, not the 404 page/],
    ["a 404 page without its policy", { notFound: { status: 404, body: NOT_FOUND_PAGE }, headers: { ...HEADERS, "content-security-policy": "default-src *" } },
      /GET \/fr\/no-such-page carries the CSP default-src \*/],
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
    assert.equal(notes.length, 3, JSON.stringify(notes));
    assert.match(notes[0], /^the revision's clock read \S+: \d{4}-\d{2}-\d{2} in Luxembourg, UTC\+0[12]:00, /);
    assert.equal(notes[1], `an accented title: "${TITLE}" (${WORK}), asked as published, in capitals and decomposed`);
    // This stand-in's release holds no legal page: said, not failed, until the opening (the plan's item 7.1).
    assert.match(notes[2], /^the legal pages are not ready to publish, so the site may not open \(deploy\.ps1 -Open and -Public refuse it\): the release holds no \/fr\/legal-notice\.html; /);
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

test("a title the exact-reference phase reads as its act's type and date passes when the phase opens that act, and fails when it opens another or none", async () => {
  // The release of 2026-10-18 found it: "Loi du 3 décembre 2015 relative à …" was answered by 4.2's phase, not the ladder.
  for (const [what, reference, expected] of [
    ["the phase opens the work", WORK_KEY, []],
    ["the phase opens another act", "loi-2017-12-21-a1118", [`search for "${TITLE}" names the reference phase's opens of loi-2017-12-21-a1118, not ${WORK} alone`]],
    ["the phase opens nothing", null, [`search for "${TITLE}" names the reference phase's not_opened of no act, not ${WORK} alone`]],
  ]) {
    const served = await revision({ reference });
    try {
      assert.deepEqual(await accentedTitleFailures(served.origin), expected, what);
    } finally {
      await served.close();
    }
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
  assert.deepEqual([...files.keys()].sort(), ["evaluation-card.json", "fr/index.html", "not-found/fr.html"]);
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

// The legal pages (the plan's item 7.1; `legal-pages.mjs`): a release's ten, each with what it says. A clean page names no
// person and gives no contact (the owner's decision of 2026-10-19), and may link the regulator a reader may complain to, as
// the privacy notice does; the other gives an e-mail address in a mail link, as the pages did before that decision.
const OFF_THE_SITE = /shows an e-mail address, a mail or telephone link, which the owner's decision of 2026-10-19 keeps off the site/;
const CLEAN = 'Lex is an independent, non-commercial tool. You may lodge a complaint with the Commission nationale pour la protection des données (CNPD), <a href="https://cnpd.public.lu/" rel="external">cnpd.public.lu</a>.';
const legalPage = (path, clean) => Buffer.from(`<!doctype html><title>${path}</title><p>${clean ? CLEAN : 'Contact: <a href="mailto:someone@example.invalid">someone@example.invalid</a>.'}</p>\n`);
const legalWeb = (clean) => LEGAL_PAGE_PATHS.map((path) => [path.slice(1), legalPage(path, clean)]);
const servedLegal = (web, change = () => ({})) => new Map(web.map(([file, body]) => [`/${file}`, { body, ...change(`/${file}`) }]));

test("a revision serves the release's own legal pages; one showing a contact is said beside the pass, and fails a -Public deployment's probe", async () => {
  for (const clean of [true, false]) {
    const web = [...WEB, ...legalWeb(clean)];
    const served = await revision({ legal: servedLegal(legalWeb(clean)) });
    try {
      const notes = [];
      assert.deepEqual(await revisionFailures(served.origin, { web, card: CARD, corpusSha256: CORPUS, notes }), [], `clean ${clean}: the private copy's probe passes`);
      const said = notes.filter((note) => note.startsWith("the legal pages are not ready to publish"));
      assert.equal(said.length, clean ? 0 : 1, `clean ${clean}: ${JSON.stringify(notes)}`);
      if (!clean) assert.match(said[0], /\/fr\/cookies\.html shows an e-mail address, a mail or telephone link/, "each page is named");
      const opening = await revisionFailures(served.origin, { web, card: CARD, corpusSha256: CORPUS, opening: true });
      assert.equal(opening.length, clean ? 0 : LEGAL_PAGE_PATHS.length, `clean ${clean}, opening: ${JSON.stringify(opening)}`);
      assert.ok(opening.every((failure) => /^the legal pages are not ready to publish, so the site may not open: \/(?:en|fr)\/[a-z-]+\.html shows /.test(failure) && OFF_THE_SITE.test(failure)), JSON.stringify(opening));
    } finally {
      await served.close();
    }
  }
  // A release that holds no legal page at all (one built before them) may be deployed privately, never publicly.
  const bare = await revision();
  try {
    assert.deepEqual(await revisionFailures(bare.origin, { web: WEB, card: CARD, corpusSha256: CORPUS }), []);
    const opening = await revisionFailures(bare.origin, { web: WEB, card: CARD, corpusSha256: CORPUS, opening: true });
    assert.deepEqual(opening, LEGAL_PAGE_PATHS.map((path) => `the legal pages are not ready to publish, so the site may not open: the release holds no ${path}`));
  } finally {
    await bare.close();
  }
});

test("a legal page served with other bytes, without the page's CSP or with a cookie fails, and so does a home page with a cookie", async () => {
  const web = [...WEB, ...legalWeb(true)];
  const cases = [
    ["another page at the address", { legal: servedLegal(legalWeb(true), (path) => (path === "/en/privacy.html" ? { body: Buffer.from("<!doctype html><p>V2's privacy page</p>") } : {})) }, /^GET \/en\/privacy\.html answered 200 with a body that is not the release image's own legal page/],
    ["no legal page served", { legal: new Map() }, /^GET \/en\/legal-notice\.html answered 404, not the legal page$/],
    ["a page without the page's CSP", { legal: servedLegal(legalWeb(true), (path) => (path === "/fr/terms.html" ? { headers: { "content-security-policy": "default-src *" } } : {})) }, /^GET \/fr\/terms\.html carries the CSP default-src \*/],
    ["a page that sets a cookie", { legal: servedLegal(legalWeb(true), (path) => (path === "/fr/cookies.html" ? { headers: { "set-cookie": "affinity=1" } } : {})) }, /^GET \/fr\/cookies\.html sets a cookie/],
    ["a home page that sets a cookie", { legal: servedLegal(legalWeb(true)), headers: { ...HEADERS, "set-cookie": "affinity=1" } }, /^GET \/fr\/ sets a cookie/],
  ];
  for (const [what, change, expected] of cases) {
    const served = await revision(change);
    try {
      const failures = await revisionFailures(served.origin, { web, card: CARD, corpusSha256: CORPUS });
      assert.ok(failures.some((failure) => expected.test(failure)), `${what}: ${JSON.stringify(failures)}`);
    } finally {
      await served.close();
    }
  }
  // Held alone, as the probe holds them.
  const served = await revision({ legal: servedLegal(legalWeb(true)) });
  try {
    assert.deepEqual(await legalPageFailures(served.origin, legalWeb(true), { opening: true }), []);
  } finally {
    await served.close();
  }
});

test("the opening's check fails closed: a page showing a contact or a placeholder, a page that does not answer as a page, a redirect, a cookie, or a site it cannot reach", async () => {
  const clean = await revision({ legal: servedLegal(legalWeb(true)) });
  try {
    assert.deepEqual(await openingFailures(clean.origin), [], "ten pages that name no person and give no contact, each linking the regulator a reader may complain to, served as pages without a cookie: the site may open");
  } finally {
    await clean.close();
  }
  const page = (path, body) => (one) => (one === path ? { body: Buffer.from(body) } : {});
  const cases = [
    ["an e-mail address in a mail link, on every page", { legal: servedLegal(legalWeb(false)) }, OFF_THE_SITE, LEGAL_PAGE_PATHS.length],
    ["an e-mail address alone", { legal: servedLegal(legalWeb(true), page("/fr/privacy.html", "<p>Écrivez à someone@example.invalid.</p>")) }, /^\/fr\/privacy\.html shows an e-mail address, which/, 1],
    ["a telephone link", { legal: servedLegal(legalWeb(true), page("/en/legal-notice.html", '<p><a href="tel:+352000000">Call</a></p>')) }, /^\/en\/legal-notice\.html shows a mail or telephone link, which/, 1],
    ["a placeholder a template left", { legal: servedLegal(legalWeb(true), page("/fr/legal-notice.html", "<p>Lex est édité par «PUBLISHER_NAME».</p>")) }, /^\/fr\/legal-notice\.html shows a placeholder a template left \(«LIKE_THIS»\), which/, 1],
    ["a postal address", { legal: servedLegal(legalWeb(true), page("/en/privacy.html", "<p>1, rue de l’Exemple, L-0000 Exempleville</p>")) }, /^\/en\/privacy\.html shows a Luxembourg postcode, as a postal address writes it, which/, 1],
    ["a page missing", { legal: servedLegal(legalWeb(true), (path) => (path === "/en/terms.html" ? { status: 404 } : {})) }, /^\/en\/terms\.html answered 404 text\/html; charset=utf-8, not 200 with a page$/, 1],
    // A redirect is never followed (the review of #990): the page at the address is what the public reads, so one sent on,
    // even to a clean legal page of the same site, or to a page elsewhere, keeps the site private.
    ["a redirect to a clean legal page of the site", { legal: servedLegal(legalWeb(true), (path) => (path === "/en/privacy.html" ? { status: 302, headers: { location: "/en/legal-notice.html" }, body: Buffer.alloc(0) } : {})) },
      /^\/en\/privacy\.html answered 302 text\/html; charset=utf-8, not 200 with a page$/, 1],
    ["a permanent redirect to another site", { legal: servedLegal(legalWeb(true), (path) => (path === "/fr/terms.html" ? { status: 308, headers: { location: "https://example.invalid/fr/terms.html" }, body: Buffer.alloc(0) } : {})) },
      /^\/fr\/terms\.html answered 308 text\/html; charset=utf-8, not 200 with a page$/, 1],
    ["a cookie", { legal: servedLegal(legalWeb(true), (path) => (path === "/en/accessibility.html" ? { headers: { "set-cookie": "affinity=1" } } : {})) }, /^\/en\/accessibility\.html sets a cookie/, 1],
  ];
  for (const [what, change, expected, count] of cases) {
    const served = await revision(change);
    try {
      const failures = await openingFailures(served.origin);
      assert.equal(failures.length, count, `${what}: ${JSON.stringify(failures)}`);
      assert.ok(failures.every((failure) => expected.test(failure)), `${what}: ${JSON.stringify(failures)}`);
    } finally {
      await served.close();
    }
  }
  // Nothing listens there: every page is a failure, so a site whose pages cannot be read is never opened.
  const unreachable = await openingFailures("http://127.0.0.1:9");
  assert.equal(unreachable.length, LEGAL_PAGE_PATHS.length);
  assert.ok(unreachable.every((failure) => / could not be asked \(.+\), so the site is not opened$/.test(failure)), JSON.stringify(unreachable));
});

test("the probe's command line runs the opening's check alone with --opening and no release: it passes over clean pages, and fails on a contact, a cookie or a site it cannot read", async () => {
  const run = (args) => new Promise((resolve) => {
    execFile(process.execPath, [fileURLToPath(new URL("../scripts/deploy-probe.mjs", import.meta.url)), ...args], { timeout: 60_000 }, (error, stdout, stderr) => {
      resolve({ code: error?.code ?? 0, stdout, stderr });
    });
  });
  for (const [clean, code] of [[true, 0], [false, 1]]) {
    const served = await revision({ legal: servedLegal(legalWeb(clean)) });
    try {
      const result = await run(["--opening", "--origin", served.origin]);
      assert.equal(result.code, code, `clean ${clean}: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, clean ? /are ready to publish: each of the 10 answers, sets no cookie and shows no e-mail address, mail or telephone link, placeholder or postcode/ : /^10 reason\(s\) the site may not open$/m);
    } finally {
      await served.close();
    }
  }
  const cookie = await revision({ legal: servedLegal(legalWeb(true), (path) => (path === "/fr/terms.html" ? { headers: { "set-cookie": "affinity=1" } } : {})) });
  try {
    const result = await run(["--opening", "--origin", cookie.origin]);
    assert.equal(result.code, 1, `a cookie: ${result.stdout}${result.stderr}`);
    assert.match(result.stderr, /^- \/fr\/terms\.html sets a cookie/m);
  } finally {
    await cookie.close();
  }
  const unread = await run(["--opening", "--origin", "http://127.0.0.1:9"]);
  assert.equal(unread.code, 1, `a site it cannot read: ${unread.stdout}${unread.stderr}`);
  assert.match(unread.stdout, /^10 reason\(s\) the site may not open$/m);
});
