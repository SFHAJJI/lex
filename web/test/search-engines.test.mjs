// What the site says to search engines (the specification of record's R67; the plan's item 3.6; `search-engines.mjs`): the
// release's switch in both states, robots.txt, the sitemap and its hreflang, noindex on search, the canonical rule for a
// permalink showing today's version, and the schema.org Legislation markup drawn from the answer. The table is shared with
// the API's tests (V3WebRootSearchEnginesTests), so the live server and the API answer one set of rules.

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { createServer } from "node:http";
import {
  INDEXED, KEPT_OUT, KEPT_OUT_META, NEVER_INDEXED_META, ROBOTS_INDEXED, ROBOTS_KEPT_OUT, SEARCH_ENGINES_VARIABLE, SITEMAP_MAXIMUM_URLS, X_ROBOTS_TAG,
  dossierHolds, dossierIdentifierOf, indexablePage, mountedBuiltAt, readIndexingRecord, robotsTxt, searchEngineFacts, searchEnginesState, servedPage,
  sitemapFiles, workAddressOf,
} from "../scripts/search-engines.mjs";
import { readingStructuredData, dossierStructuredData } from "../scripts/structured-data.mjs";
import { readDossierAnswer } from "../scripts/dossier-answer.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { parseSitePath } from "../scripts/permalinks.mjs";
import { answersToday, readingAddressShown, todayAt } from "../scripts/initial-answer.mjs";
import { readEvidenceBundleAnswer } from "../scripts/reading-answer.mjs";
import {
  CENSUS_EVALUATION_CARD, renderLiveComparePage, renderLiveCoveragePage, renderLiveDossierPage, renderLiveExportPage, renderLiveHistoryPage, renderLiveHomePage,
  renderLiveLocaleUnavailablePage, renderLiveMaintenancePage, renderLiveNotFoundPage, renderLiveRadarPage, renderLiveReadingPage, renderLiveSearchPage,
} from "../.react-build/app.mjs";
import { LIVE_CONTRACT as CONTRACT } from "../.react-build/app.mjs";
import { homeFacts } from "../scripts/live-home.mjs";

const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json", import.meta.url), "utf8"));
const ENGINES = TABLE.searchEngines;
const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope;
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const STATE = READING.result.value.states[0];
const EUROPE_READING = envelopeOf("evidence_bundle", "an EU wording's permalink whose next wording's text is not held");
const DOSSIER = envelopeOf("dossier", "the work, in the language it is held in");
const EUROPE_DOSSIER = envelopeOf("dossier", "one EU work by its CELEX");
const UNKNOWN_DOSSIER = envelopeOf("dossier", "a work the index does not hold");
const REFUSED_READING = envelopeOf("evidence_bundle", "a date before the work's history");
const COVERAGE = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
const UNMOUNTED_COVERAGE = envelopeOf("coverage", "no corpus mounted");
const ORIGIN = "https://law.soufien.lu";

/** A page's head. */
const headOf = (html) => html.slice(0, html.indexOf("</head>"));
/** The robots meta elements a page names. */
const robotsMetas = (html) => headOf(html).match(/<meta name="robots"[^>]*\/>/g) ?? [];
/** The one canonical link a page names. */
const canonicalOf = (html) => {
  const links = [...headOf(html).matchAll(/<link rel="canonical" href="([^"]*)"\/>/g)].map((match) => match[1]);
  assert.ok(links.length <= 1, "at most one canonical link");
  return links[0] ?? null;
};
/** The schema.org markup a page carries, read back, or null. */
const markupOf = (html) => {
  const blocks = [...headOf(html).matchAll(/<script type="application\/ld\+json">([^<]*)<\/script>/g)].map((match) => JSON.parse(match[1]));
  assert.ok(blocks.length <= 1, "at most one markup block");
  return blocks[0] ?? null;
};
/** The table's answer to one of a server's own questions (`searchEngineFacts`), or null. */
const tableAnswer = (operation, parameters) =>
  ENGINES.answers.find((one) => one.operation === operation && JSON.stringify(one.parameters) === JSON.stringify(parameters))?.envelope ?? null;
/** The record of the works the table's API holds, and its lastmod. */
const HELD = await searchEngineFacts(readIndexingRecord(JSON.stringify(ENGINES.record)), async (operation, parameters) => tableAnswer(operation, parameters));

/** The same envelope with its first state's (or wording's) members changed. */
function withVersion(envelope, change) {
  const copy = structuredClone(envelope);
  const value = copy.result.value;
  const list = value.states ?? value.wordings;
  list[0] = { ...list[0], ...change };
  return copy;
}

test("the switch is the release's one variable, kept out unless it says indexed, exactly, as the shared table says", () => {
  assert.equal(SEARCH_ENGINES_VARIABLE, ENGINES.variable);
  assert.ok(ENGINES.states.length >= 6);
  for (const { value, state } of ENGINES.states) assert.equal(searchEnginesState(value ?? undefined), state, JSON.stringify(value));
  assert.equal(searchEnginesState(undefined), KEPT_OUT, "no variable: kept out (O27 is not answered)");
  assert.deepEqual({ never: NEVER_INDEXED_META, keptOut: KEPT_OUT_META }, ENGINES.meta);
  assert.equal(X_ROBOTS_TAG, ENGINES.xRobotsTag);
});

test("robots.txt, in both states, disallows the API, MCP, the health routes, search and export and allows the pages; indexed, it names the sitemap (R67)", () => {
  assert.equal(robotsTxt(KEPT_OUT), ENGINES.robots["kept-out"]);
  assert.equal(robotsTxt(INDEXED), ENGINES.robots.indexed);
  for (const robots of [ROBOTS_KEPT_OUT, ROBOTS_INDEXED]) {
    for (const line of ["User-agent: *", "Disallow: /api/", "Disallow: /mcp", "Disallow: /health/", "Disallow: /fr/search.html", "Disallow: /en/search.html", "Disallow: /fr/export.html", "Disallow: /en/export.html"]) {
      assert.ok(robots.split("\n").includes(line), line);
    }
    // Kept out too, no page of a law, nor a home page, is disallowed: a crawler must fetch a page to read its noindex, which
    // a blocked page never is, and a blocked address can still be listed bare (review of #976, F1).
    assert.ok(!robots.split("\n").includes("Disallow: /"), "never everything");
    const disallowed = robots.split("\n").filter((line) => line.startsWith("Disallow: ")).map((line) => line.slice("Disallow: ".length));
    for (const path of ["/fr/", "/en/index.html", "/en/lu-legilux/loi-1991-08-10-n3", "/fr/eu-eurlex/32016R0679/fra", "/en/coverage.html", "/fr/reading.html"]) {
      assert.ok(!disallowed.some((prefix) => path.startsWith(prefix)), `${path} is allowed`);
    }
  }
  assert.ok(ROBOTS_INDEXED.split("\n").includes(`Sitemap: ${ORIGIN}/sitemap.xml`), "indexed: the sitemap named");
  assert.ok(!ROBOTS_KEPT_OUT.includes("Sitemap:"), "kept out: no sitemap named");
  assert.equal(ROBOTS_INDEXED, `${ROBOTS_KEPT_OUT}\nSitemap: ${ORIGIN}/sitemap.xml\n`, "the same rules");
});

test("the indexing record is read whole or refused whole, as the shared table says, whatever its size", () => {
  assert.ok(ENGINES.admitted.length >= 3 && ENGINES.refused.length >= 10);
  for (const { what, record } of ENGINES.admitted) assert.notEqual(readIndexingRecord(JSON.stringify(record)), null, what);
  for (const { what, record } of ENGINES.refused) assert.equal(readIndexingRecord(JSON.stringify(record)), null, what);
  for (const text of ["", "not json", "null", "42", "\"lex-v3-indexing-record/1\""]) assert.equal(readIndexingRecord(text), null, JSON.stringify(text));
  assert.notEqual(readIndexingRecord(`\uFEFF${JSON.stringify(ENGINES.record)}`), null, "a byte-order mark is no part of it");
  const record = readIndexingRecord(JSON.stringify(ENGINES.record));
  assert.equal(record.builtAt, ENGINES.record.built_at);
  assert.deepEqual(record.works.map((work) => work.address), ENGINES.record.works.map((work) => work.address));
  // A record whose sitemap holds more addresses than one file may is read whole, and its sitemap split (review of #976, F3:
  // R92's gate does not hang on a sitemap's size).
  const perWork = 2 * (1 + 1);
  const many = Math.floor((SITEMAP_MAXIMUM_URLS - 2 * 2) / perWork) + 1;
  const works = Array.from({ length: many }, (_, index) => ({ address: `/lu-legilux/w${String(index).padStart(6, "0")}`, languages: ["fra"] }));
  const large = readIndexingRecord(JSON.stringify({ schema: ENGINES.record.schema, built_at: null, works }));
  assert.equal(large?.works.length, many, "over one sitemap file's limit: read whole");
  const files = sitemapFiles(large);
  assert.deepEqual(Object.keys(files), ["/sitemap.xml", "/sitemap-1.xml", "/sitemap-2.xml"], "an index and two parts");
  const count = (text) => (text.match(/<url>/g) ?? []).length;
  assert.equal(count(files["/sitemap-1.xml"]), SITEMAP_MAXIMUM_URLS);
  assert.equal(count(files["/sitemap-1.xml"]) + count(files["/sitemap-2.xml"]), 2 * (2 + many * 2), "every address, once");
  assert.equal(indexablePage(INDEXED, `/en${works.at(-1).address}`, large), true, "and every listed work opened");
});

test("the server's own questions: the mounted index's build time from coverage, and which listed works the API's dossier answers (review of #976, F5)", async () => {
  assert.ok(ENGINES.builds.length >= 5 && ENGINES.holds.length >= 3);
  for (const { what, envelope, builtAt } of ENGINES.builds) assert.equal(mountedBuiltAt(envelope), builtAt, what);
  for (const { what, envelope, held } of ENGINES.holds) assert.equal(dossierHolds(envelope), held, what);
  // The census's own coverage answer names its mounted build.
  assert.equal(mountedBuiltAt(COVERAGE), COVERAGE.result.value.history.snapshots_with_text.find((one) => one.retained_as.includes("mounted")).built_at);
  assert.equal(mountedBuiltAt(UNMOUNTED_COVERAGE), null);
  assert.equal(dossierHolds(DOSSIER), true);
  assert.equal(dossierHolds(UNKNOWN_DOSSIER), false);
  assert.equal(dossierIdentifierOf("/eu-eurlex/32016R0679"), "32016R0679", "an EU act is asked by its CELEX");
  assert.equal(dossierIdentifierOf("/lu-legilux/loi-1991-08-10-n3"), "/lu-legilux/loi-1991-08-10-n3");
  assert.equal(HELD.lastmod, ENGINES.lastmod);
  assert.deepEqual(HELD.record.works.map((work) => work.address), ENGINES.held, "a work the API does not answer is dropped");
  assert.ok(ENGINES.record.works.some((work) => !ENGINES.held.includes(work.address)), "the table's record names one");
  assert.deepEqual(await searchEngineFacts(null, async (operation, parameters) => tableAnswer(operation, parameters)), { record: null, lastmod: ENGINES.lastmod }, "no record");
  assert.deepEqual(await searchEngineFacts(HELD.record, async () => null), { record: { builtAt: ENGINES.record.built_at, works: [] }, lastmod: null }, "no answer: nothing held");
});

test("the sitemap lists the home pages, Trust and Coverage, and each held work's dossier and readable addresses in both languages, with their alternates, never a permalink (R67)", () => {
  assert.deepEqual(sitemapFiles(HELD.record, { lastmod: HELD.lastmod }), ENGINES.sitemap);
  assert.deepEqual(sitemapFiles(null, { lastmod: HELD.lastmod }), ENGINES.sitemapWithoutRecord);
  assert.deepEqual(sitemapFiles(HELD.record, { lastmod: HELD.lastmod, partSize: ENGINES.split.partSize }), ENGINES.split.files);
  // The index names each part, dated by the mounted build.
  assert.deepEqual(Object.keys(ENGINES.sitemap), ["/sitemap.xml", "/sitemap-1.xml"]);
  assert.match(ENGINES.sitemap["/sitemap.xml"], /^<\?xml version="1\.0" encoding="UTF-8"\?>\n<sitemapindex xmlns="http:\/\/www\.sitemaps\.org\/schemas\/sitemap\/0\.9">\n<sitemap><loc>https:\/\/law\.soufien\.lu\/sitemap-1\.xml<\/loc><lastmod>2026-10-01T08:00:00Z<\/lastmod><\/sitemap>\n<\/sitemapindex>\n$/);
  const parts = Object.keys(ENGINES.split.files).slice(1);
  assert.equal(parts.length, 3, "14 addresses in parts of at most 5");
  assert.deepEqual(parts.flatMap((path) => ENGINES.split.files[path].split("\n").filter((line) => line.startsWith("<url>"))), ENGINES.sitemap["/sitemap-1.xml"].split("\n").filter((line) => line.startsWith("<url>")), "the same entries, in order");
  for (const path of parts) assert.ok(ENGINES.split.files["/sitemap.xml"].includes(`<loc>${ORIGIN}${path}</loc>`), `the index names ${path}`);
  // A part is cut at the protocol's byte limit too.
  const byBytes = sitemapFiles(HELD.record, { lastmod: HELD.lastmod, maximumBytes: 2000 });
  assert.ok(Object.keys(byBytes).length > 2, "split by bytes");
  for (const [path, text] of Object.entries(byBytes).slice(1)) assert.ok(text.length <= 2000, `${path}: ${text.length} bytes`);
  const sitemap = ENGINES.sitemap["/sitemap-1.xml"];
  const locs = [...sitemap.matchAll(/<loc>([^<]*)<\/loc>/g)].map((match) => match[1]);
  const held = ENGINES.record.works.filter((work) => ENGINES.held.includes(work.address));
  assert.equal(locs.length, 2 * (2 + held.reduce((count, work) => count + 1 + work.languages.length, 0)));
  assert.equal(new Set(locs).size, locs.length, "each address once");
  for (const loc of locs) {
    assert.ok(loc.startsWith(`${ORIGIN}/fr/`) || loc.startsWith(`${ORIGIN}/en/`), `${loc}: under an interface language, a page that answers itself`);
    const named = parseSitePath(loc.slice(ORIGIN.length));
    if (named !== null) assert.ok(named.kind === "work" || (named.kind === "current" && named.anchor === null), `${loc}: a dossier or an act's readable address, never a permalink`);
  }
  for (const work of held) {
    assert.ok(locs.includes(`${ORIGIN}/fr${work.address}`) && locs.includes(`${ORIGIN}/en${work.address}`), `${work.address}: its dossier`);
    for (const language of work.languages) assert.ok(locs.includes(`${ORIGIN}/en${work.address}/${language}`), `${work.address}: its readable address in ${language}`);
  }
  assert.ok(!sitemap.includes("no-such-work"), "a work the mount does not hold is never listed (review of #976, F5)");
  // Each entry names its page in each interface language and, for any other, the address with no interface language.
  for (const entry of sitemap.split("\n").filter((line) => line.startsWith("<url>"))) {
    const loc = /<loc>([^<]*)<\/loc>/.exec(entry)[1];
    const links = [...entry.matchAll(/<xhtml:link rel="alternate" hreflang="([^"]*)" href="([^"]*)"\/>/g)].map(([, hreflang, href]) => [hreflang, href]);
    assert.deepEqual(links.map(([hreflang]) => hreflang), ["fr", "en", "x-default"], loc);
    assert.ok(links.some(([, href]) => href === loc), `${loc}: names itself among its alternates`);
    assert.match(entry, /<lastmod>2026-10-01T08:00:00Z<\/lastmod>/, `${loc}: lastmod is the mounted index's build, never a version's date nor the record's`);
  }
  const bare = ENGINES.sitemapWithoutRecord["/sitemap-1.xml"];
  assert.ok(!bare.includes("lu-legilux") && !bare.includes("eu-eurlex"), "no record: no law's page");
  assert.ok(!sitemapFiles(null)["/sitemap-1.xml"].includes("<lastmod>"), "and with no build, no date");
});

test("a page with the kept-out marker is opened only with the site indexed, and a law's page only for a held work the record lists (R92), as the shared table says", () => {
  const record = HELD.record;
  assert.ok(ENGINES.pages.length >= 10);
  for (const { path, opened } of ENGINES.pages) {
    assert.equal(indexablePage(KEPT_OUT, path, record), opened["kept-out"], `${path}, kept out`);
    assert.equal(indexablePage(INDEXED, path, record), opened.indexed, `${path}, indexed`);
    assert.equal(indexablePage(INDEXED, path, null), opened["indexed, with no record"], `${path}, indexed, with no record`);
    const page = `<!doctype html><html><head>${KEPT_OUT_META}</head><body></body></html>`;
    const served = servedPage(page, { state: INDEXED, pathname: path, record });
    assert.equal(served.opened, opened.indexed, path);
    assert.equal(served.html, opened.indexed ? "<!doctype html><html><head></head><body></body></html>" : page, path);
    // A page that names it never is never opened.
    const never = `<!doctype html><html><head>${NEVER_INDEXED_META}</head></html>`;
    assert.deepEqual(servedPage(never, { state: INDEXED, pathname: path, record }), { html: never, opened: false }, `${path}: never`);
  }
  assert.equal(workAddressOf("/fr/eu-eurlex/32016R0679/eng/art_5%281%29"), "/eu-eurlex/32016R0679");
  assert.equal(workAddressOf(`/lu-legilux/loi-1991-08-10-n3/2024-02-01--${STATE.state_sha256}/fra`), "/lu-legilux/loi-1991-08-10-n3");
  assert.equal(workAddressOf("/en/search.html"), null);
});

test("every live page names one robots noindex as built: the switch opens the home pages, Trust and Coverage and a law's page rendered whole, never search, export or the forms", () => {
  const never = {
    search: renderLiveSearchPage(),
    export: renderLiveExportPage(),
    history: renderLiveHistoryPage(),
    compare: renderLiveComparePage(),
    radar: renderLiveRadarPage(),
    "reading as built": renderLiveReadingPage(),
    "dossier as built": renderLiveDossierPage(),
    "the 404 page": renderLiveNotFoundPage(),
    maintenance: renderLiveMaintenancePage(),
    "German": renderLiveLocaleUnavailablePage("de"),
    "a refused reading": renderLiveReadingPage({ initial: { envelope: REFUSED_READING, path: `/en${STATE.permalink}` } }),
    "a work the index does not hold": renderLiveDossierPage({ initial: { envelope: UNKNOWN_DOSSIER, path: "/en/lu-legilux/no-such-work" } }),
  };
  // The home page as built, and rendered with no corpus mounted, is never opened (review of #976, F2).
  never["the home page as built"] = renderLiveHomePage();
  never["the home page with no corpus mounted"] = renderLiveHomePage({ facts: homeFacts(UNMOUNTED_COVERAGE, CONTRACT) });
  for (const [what, html] of Object.entries(never)) assert.deepEqual(robotsMetas(html), [NEVER_INDEXED_META], what);
  const opened = {
    home: renderLiveHomePage({ facts: homeFacts(COVERAGE, CONTRACT) }),
    coverage: renderLiveCoveragePage({ card: CENSUS_EVALUATION_CARD }),
    "a dossier": renderLiveDossierPage({ initial: { envelope: DOSSIER, path: "/en/lu-legilux/loi-1991-08-10-n3" } }),
    "a readable address": renderLiveReadingPage({ initial: { envelope: READING, path: "/en/lu-legilux/loi-1991-08-10-n3/fra/art_15" } }),
    "a permalink of today's version": renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}/art_15` } }),
  };
  for (const [what, html] of Object.entries(opened)) assert.deepEqual(robotsMetas(html), [KEPT_OUT_META], what);
  // The home page and Trust and Coverage name themselves canonical, in their language (review of #976, F2); the home page
  // is also served at index.html, which names /en/ too.
  assert.equal(canonicalOf(opened.home), `${ORIGIN}/en/`);
  assert.equal(canonicalOf(never["the home page as built"]), `${ORIGIN}/en/`);
  assert.equal(canonicalOf(opened.coverage), `${ORIGIN}/en/coverage.html`);
  for (const what of ["search", "export", "history", "the 404 page", "German"]) assert.equal(canonicalOf(never[what]), null, `${what}: no canonical`);
});

test("a permalink showing the version its act's readable address shows today names that readable address canonical; any other version names itself (R67)", () => {
  // The sample's state applies from 2024-02-01 with no later state held, and the answer was given on 2026-09-18.
  assert.equal(todayAt(READING.context.freshness.observed_at), "2026-09-18");
  const html = renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}/art_15` } });
  assert.equal(canonicalOf(html), `${ORIGIN}/en/lu-legilux/loi-1991-08-10-n3/fra/art_15`, "the article's readable address, in the page's language");
  // The test bundle is the English one: a page under /fr/ is the French bundle's, which names its canonical under /fr/.
  assert.equal(canonicalOf(renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}` } })), `${ORIGIN}/en/lu-legilux/loi-1991-08-10-n3/fra`);
  assert.equal(canonicalOf(renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}/no-such-article` } })), `${ORIGIN}/en/lu-legilux/loi-1991-08-10-n3/fra`, "an article it does not hold: the act's");

  // A version a later one replaced on or before today, or one not yet applicable, is not today's: it names itself, and is
  // never opened to search engines.
  for (const [what, envelope] of [
    ["superseded", withVersion(READING, { next_applicability_date: "2025-01-01" })],
  ]) {
    const page = renderLiveReadingPage({ initial: { envelope, path: `/en${STATE.permalink}/art_15` } });
    assert.equal(canonicalOf(page), `${ORIGIN}/en${STATE.permalink}/art_15`, what);
    assert.deepEqual(robotsMetas(page), [NEVER_INDEXED_META], what);
  }
  const view = readEvidenceBundleAnswer(READING.result.value);
  assert.equal(answersToday(view, view.states[0], "2024-02-01"), true, "from its own date");
  assert.equal(answersToday(view, view.states[0], "2024-01-31"), false, "not before it");
  assert.equal(answersToday(view, { ...view.states[0], nextApplicabilityDate: "2026-09-18" }, "2026-09-18"), false, "not from the next state's date");
  assert.equal(answersToday(view, { ...view.states[0], nextApplicabilityDate: "2026-09-19" }, "2026-09-18"), true, "until it");
  assert.equal(answersToday(view, view.states[0], null), false, "no today: no claim");
  assert.equal(readingAddressShown(view, `${STATE.permalink}/art_15`), `${STATE.permalink}/art_15`, "with no today, the permalink names itself");

  // An EU wording outside the time view answers only its own date, and one whose next wording is dated on or before today
  // is not today's (the readable address then shows the next, or refuses): each names its permalink.
  const europe = readEvidenceBundleAnswer(EUROPE_READING.result.value);
  const wording = europe.wordings[0];
  assert.equal(answersToday(europe, wording, "2026-09-18"), false, "the next wording, of 2024-01-01, answers today");
  assert.equal(answersToday(europe, { ...wording, nextDate: null }, "2026-09-18"), true, "the latest wording answers today");
  assert.equal(answersToday(europe, { ...wording, nextDate: undefined }, "2026-09-18"), false, "outside the time view, its own date only");
  assert.equal(readingAddressShown({ ...europe, wordings: [{ ...wording, nextDate: null }] }, `${wording.permalink}/001`, null, "2026-09-18"), "/eu-eurlex/32016R0679/eng/001");
});

test("the Legislation markup of a reading and a dossier holds the answer's facts only, and names no person", () => {
  const reading = markupOf(renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}/art_15` } }));
  const sources = [...new Set(STATE.articles.map((article) => article.official_source))].sort();
  assert.deepEqual(reading, {
    "@context": "https://schema.org",
    "@type": "Legislation",
    url: `${ORIGIN}/en/lu-legilux/loi-1991-08-10-n3/fra/art_15`,
    legislationIdentifier: STATE.publisher_legal_resource_iri,
    inLanguage: "fr",
    legislationJurisdiction: "LU",
    legislationDate: "1991-08-10",
    legislationDateVersion: READING.result.value.requested_date,
    isBasedOn: sources.length === 1 ? sources[0] : sources,
  }, "no name: the sample holds no title for the state's expression");

  // A title that would end the block is escaped, never written raw (review of #976, F6), and reads back whole.
  const hostile = structuredClone(READING);
  const script = "Loi </script><script>alert(1)</script> & <!-- -->";
  hostile.result.value.citation_facts.titles = [{ expression_iri: STATE.expression_iri, language: "fra", titles: [{ title: script, evidence_sha256: "c".repeat(64) }], short_titles: [] }];
  const hostilePage = renderLiveReadingPage({ initial: { envelope: hostile, path: `/en${STATE.permalink}` } });
  assert.ok(!headOf(hostilePage).includes("</script><script>"), "the block is not ended by the title");
  assert.ok(headOf(hostilePage).includes("\\u003c/script\\u003e"), "the title's < and > are escaped");
  assert.equal(markupOf(hostilePage).name, script, "and read back as written");

  // The one title held for the expression is its name, verbatim; two are none.
  const titled = structuredClone(READING);
  titled.result.value.citation_facts.titles = [{ expression_iri: STATE.expression_iri, language: "fra", titles: [{ title: "Loi du 10 août 1991 sur la profession d'avocat.", evidence_sha256: "a".repeat(64) }], short_titles: [] }];
  assert.equal(markupOf(renderLiveReadingPage({ initial: { envelope: titled, path: `/en${STATE.permalink}` } })).name, "Loi du 10 août 1991 sur la profession d'avocat.");
  titled.result.value.citation_facts.titles[0].titles.push({ title: "Another title.", evidence_sha256: "b".repeat(64) });
  assert.equal(markupOf(renderLiveReadingPage({ initial: { envelope: titled, path: `/en${STATE.permalink}` } })).name, undefined);

  const wording = EUROPE_READING.result.value.wordings[0];
  const europe = markupOf(renderLiveReadingPage({ initial: { envelope: EUROPE_READING, path: `/en${wording.permalink}/001` } }));
  assert.equal(europe.legislationIdentifier, wording.celex, "the wording's CELEX");
  assert.equal(europe.legislationJurisdiction, "EU");
  assert.equal(europe.inLanguage, "en");
  assert.equal(europe.copyrightNotice, EUROPE_READING.result.value.acknowledgement, "the acknowledgement, as the answer gives it");
  assert.equal(europe.url, `${ORIGIN}/en${wording.permalink}/001`, "not today's wording: its own permalink");
  for (const absent of ["name", "legislationDate"]) assert.equal(europe[absent], undefined, `${absent}: the EU index holds none`);

  const dossier = markupOf(renderLiveDossierPage({ initial: { envelope: DOSSIER, path: "/en/lu-legilux/loi-1991-08-10-n3" } }));
  assert.deepEqual(dossier, {
    "@context": "https://schema.org",
    "@type": "Legislation",
    url: `${ORIGIN}/en/lu-legilux/loi-1991-08-10-n3`,
    legislationIdentifier: DOSSIER.result.value.publisher_work_iri,
    inLanguage: "fr",
    legislationJurisdiction: "LU",
    legislationDate: "1991-08-10",
  });
  const europeDossier = markupOf(renderLiveDossierPage({ initial: { envelope: EUROPE_DOSSIER, path: "/en/eu-eurlex/32016R0679" } }));
  assert.deepEqual(europeDossier, {
    "@context": "https://schema.org",
    "@type": "Legislation",
    url: `${ORIGIN}/en/eu-eurlex/32016R0679`,
    legislationIdentifier: "32016R0679",
    inLanguage: "en",
    legislationJurisdiction: "EU",
  });
  // A name is in the version's own language (review of #976, F6): a title of the same expression in another language is
  // never the reading's name, and a dossier names the act as published in each language, each tagged with its own.
  const languages = {
    publisher: "lu-legilux", date: "2024-02-01",
    citationFacts: { titles: [
      { expressionIri: "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1/jo/fr", language: "deu", titles: ["Gesetz vom 1. Januar 2000"] },
      { expressionIri: "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1/jo/fr", language: "fra", titles: ["Loi du 1er janvier 2000"] },
    ] },
  };
  const version = (language) => ({
    language, expressionIri: "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1/jo/fr", articles: [],
    publisherLegalResourceIri: "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1/jo", publisherWorkIri: "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1",
  });
  assert.equal(readingStructuredData({ view: languages, version: version("fra"), canonical: "x" }).name, "Loi du 1er janvier 2000");
  assert.equal(readingStructuredData({ view: languages, version: version("deu"), canonical: "x" }).name, "Gesetz vom 1. Januar 2000");
  assert.equal(readingStructuredData({ view: languages, version: version("eng"), canonical: "x" }).name, undefined, "none held in its language");
  const bilingual = structuredClone(DOSSIER.result.value);
  const work = bilingual.publisher_work_iri;
  bilingual.titles = [
    { expression_iri: `${work}/jo/fr`, language: "fra", titles: [{ title: "Loi du 10 août 1991 sur la profession d'avocat", evidence_sha256: "d".repeat(64) }], short_titles: [] },
  ];
  assert.equal(dossierStructuredData({ view: readDossierAnswer(bilingual), canonical: "x" }).name, "Loi du 10 août 1991 sur la profession d'avocat", "one language: a plain name");
  const two = { ...readDossierAnswer(bilingual) };
  two.titles = [
    ...two.titles,
    { language: "deu", expressionIri: `${work}/jo/de`, titles: [{ title: "Gesetz vom 10. August 1991", evidenceSha256: "e".repeat(64) }], shortTitles: [] },
  ];
  two.states = [...two.states, { ...two.states[0], language: "deu", expressionIri: `${work}/jo/de` }];
  assert.deepEqual(dossierStructuredData({ view: two, canonical: "x" }).name, [
    { "@language": "de", "@value": "Gesetz vom 10. August 1991" },
    { "@language": "fr", "@value": "Loi du 10 août 1991 sur la profession d'avocat" },
  ], "two languages: each title tagged with its own");
  assert.deepEqual(dossierStructuredData({ view: two, canonical: "x" }).inLanguage, ["de", "fr"]);

  // Never a person, a legal force or a legal value: the index holds none of them.
  for (const markup of [reading, europe, dossier, europeDossier]) {
    for (const member of ["author", "legislationPassedBy", "legislationResponsible", "legislationLegalForce", "legislationLegalValue", "creator", "contributor"]) {
      assert.equal(markup[member], undefined, member);
    }
  }
  assert.equal(markupOf(renderLiveReadingPage({ initial: { envelope: REFUSED_READING, path: `/en${STATE.permalink}` } })), null, "a refusal carries none");
  assert.equal(markupOf(renderLiveSearchPage()), null);
});

/** A web root of pages that name each marker, and a record beside it. */
async function fixtureRoot() {
  const root = await mkdtemp(join(tmpdir(), "lex-search-engines-"));
  for (const language of ["en", "fr"]) {
    await mkdir(join(root, language), { recursive: true });
    await writeFile(join(root, language, "index.html"), `<!doctype html><html><head>${KEPT_OUT_META}</head><body>home</body></html>`);
    await writeFile(join(root, language, "search.html"), `<!doctype html><html><head>${NEVER_INDEXED_META}</head><body>search</body></html>`);
    // A law's page as the server would render it whole: the kept-out marker (the API is unreachable, so it is served as built).
    await writeFile(join(root, language, "dossier.html"), `<!doctype html><html><head>${KEPT_OUT_META}</head><body>dossier</body></html>`);
  }
  await writeFile(join(root, "styles.css"), "body{}");
  await mkdir(join(root, "not-found"), { recursive: true });
  for (const language of ["en", "fr"]) await writeFile(join(root, "not-found", `${language}.html`), `<!doctype html><html><head>${NEVER_INDEXED_META}</head><body>404</body></html>`);
  const record = join(root, "..", `${root.split(/[\\/]/).pop()}-record.json`);
  await writeFile(record, JSON.stringify(ENGINES.record));
  return { root, record };
}

async function listening(server) {
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
}

/** A stand-in API that answers the server's own questions as the table says, and counts them. */
async function standInApi() {
  const asked = [];
  const server = createServer((request, response) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => {
      const operation = request.url.slice("/api/v3/".length);
      const { parameters } = JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
      asked.push(`${operation} ${JSON.stringify(parameters)}`);
      const envelope = tableAnswer(operation, parameters);
      if (envelope === null) {
        response.writeHead(404);
        response.end();
        return;
      }
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify(envelope));
    });
  });
  const origin = await listening(server);
  return { origin, asked, close: () => new Promise((resolve) => server.close(resolve)) };
}

async function serve(options) {
  const server = createLiveServer({ apiOrigin: "http://127.0.0.1:9", ...options });
  const origin = await listening(server);
  return { origin, close: () => new Promise((resolve) => server.close(resolve)) };
}

test("the live server says the switch's state: robots.txt, the sitemap, X-Robots-Tag and the robots element, in both states, as the shared table says", async () => {
  const { root, record } = await fixtureRoot();
  const api = await standInApi();
  const rows = TABLE.paths.filter((row) => row.searchEngines !== undefined);
  assert.deepEqual(rows.map((row) => row.path), ["/robots.txt", "/sitemap.xml", "/sitemap-1.xml", "/sitemap-2.xml"]);
  try {
    for (const [state, options] of [[KEPT_OUT, { searchEngines: undefined }], [KEPT_OUT, { searchEngines: "Indexed" }], [INDEXED, { searchEngines: "indexed", indexingRecord: record }]]) {
      const live = await serve({ root: pathToFileURL(`${root}/`), apiOrigin: api.origin, ...options });
      try {
        for (const row of rows) {
          const expected = row.searchEngines[state];
          const answer = await fetch(`${live.origin}${row.path}`);
          const body = await answer.text();
          assert.equal(answer.status, expected.status, `${state} ${row.path}`);
          if (!expected.claimed) continue;
          assert.equal(answer.headers.get("content-type"), expected.type, `${state} ${row.path}`);
          assert.equal(answer.headers.get("x-robots-tag"), X_ROBOTS_TAG, `${state} ${row.path}: no search result of its own`);
          assert.equal(body, expected.body === "robots" ? ENGINES.robots[state] : ENGINES.sitemap[expected.body], `${state} ${row.path}`);
          const head = await fetch(`${live.origin}${row.path}`, { method: "HEAD" });
          assert.equal(await head.text(), "", `${state} ${row.path}: HEAD carries no body`);
        }
        const pages = [
          ["/en/", state === INDEXED],
          ["/fr/", state === INDEXED],
          ["/en/search.html", false],
          ["/en/lu-legilux/loi-1991-08-10-n3", state === INDEXED],
          ["/en/lu-legilux/loi-2017-03-14-a439", false],
          // Listed, but the API's dossier does not answer it: never opened (review of #976, F5).
          ["/en/lu-legilux/no-such-work", false],
        ];
        for (const [path, opened] of pages) {
          const answer = await fetch(`${live.origin}${path}`);
          const body = await answer.text();
          assert.equal(answer.status, 200, path);
          assert.equal(answer.headers.get("x-robots-tag"), opened ? null : X_ROBOTS_TAG, `${state} ${path}`);
          assert.equal(body.includes('<meta name="robots"'), !opened, `${state} ${path}`);
          assert.equal(Number(answer.headers.get("content-length")), Buffer.byteLength(body), `${state} ${path}: its length as served`);
        }
        for (const path of ["/styles.css", "/no-such-page"]) {
          const answer = await fetch(`${live.origin}${path}`, { redirect: "manual" });
          assert.equal(answer.headers.get("x-robots-tag"), X_ROBOTS_TAG, `${state} ${path}: every other answer is kept out`);
        }
        // A redirect or a move: kept out, the header; indexed, none (it is no page).
        assert.ok(ENGINES.redirects.paths.length >= 3);
        for (const path of ENGINES.redirects.paths) {
          const answer = await fetch(`${live.origin}${path}`, { redirect: "manual" });
          assert.ok(answer.status === 301 || answer.status === 302, `${path}: ${answer.status}`);
          assert.equal(answer.headers.get("x-robots-tag"), ENGINES.redirects[state], `${state} ${path}`);
        }
      } finally {
        await live.close();
      }
    }
    // The API was asked once, whatever was served since: its coverage and each listed work's dossier (review of #976, F4).
    assert.deepEqual(api.asked, ENGINES.answers.map((one) => `${one.operation} ${JSON.stringify(one.parameters)}`));
    // Indexed with no record: the home pages are opened, no law's page, and the sitemap lists none.
    const live = await serve({ root: pathToFileURL(`${root}/`), apiOrigin: api.origin, searchEngines: "indexed" });
    try {
      assert.equal(await (await fetch(`${live.origin}/sitemap-1.xml`)).text(), ENGINES.sitemapWithoutRecord["/sitemap-1.xml"]);
      assert.equal((await fetch(`${live.origin}/en/`)).headers.get("x-robots-tag"), null);
      assert.equal((await fetch(`${live.origin}/en/lu-legilux/loi-1991-08-10-n3`)).headers.get("x-robots-tag"), X_ROBOTS_TAG);
    } finally {
      await live.close();
    }
    // Indexed with an API it cannot reach: nothing is opened, no sitemap is offered, and it asks again on the next request.
    const unreached = await serve({ root: pathToFileURL(`${root}/`), searchEngines: "indexed", indexingRecord: record });
    try {
      assert.equal((await fetch(`${unreached.origin}/sitemap.xml`)).status, 404);
      assert.equal((await fetch(`${unreached.origin}/en/`)).headers.get("x-robots-tag"), X_ROBOTS_TAG, "fail closed");
    } finally {
      await unreached.close();
    }
  } finally {
    await api.close();
    await rm(record, { force: true });
    await rm(root, { recursive: true, force: true });
  }
});
