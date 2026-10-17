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

import {
  INDEXED, KEPT_OUT, KEPT_OUT_META, NEVER_INDEXED_META, ROBOTS_INDEXED, ROBOTS_KEPT_OUT, SEARCH_ENGINES_VARIABLE, SITEMAP_MAXIMUM_URLS, X_ROBOTS_TAG,
  indexablePage, readIndexingRecord, robotsTxt, searchEnginesState, servedPage, sitemapXml, workAddressOf,
} from "../scripts/search-engines.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { parseSitePath } from "../scripts/permalinks.mjs";
import { answersToday, readingAddressShown, todayAt } from "../scripts/initial-answer.mjs";
import { readEvidenceBundleAnswer } from "../scripts/reading-answer.mjs";
import {
  CENSUS_EVALUATION_CARD, renderLiveComparePage, renderLiveCoveragePage, renderLiveDossierPage, renderLiveExportPage, renderLiveHistoryPage, renderLiveHomePage,
  renderLiveLocaleUnavailablePage, renderLiveMaintenancePage, renderLiveNotFoundPage, renderLiveRadarPage, renderLiveReadingPage, renderLiveSearchPage,
} from "../.react-build/app.mjs";

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

test("robots.txt disallows everything kept out, and indexed the API, MCP, the health routes, search and export, naming the sitemap (R67)", () => {
  assert.equal(robotsTxt(KEPT_OUT), ENGINES.robots["kept-out"]);
  assert.equal(robotsTxt(INDEXED), ENGINES.robots.indexed);
  assert.equal(ROBOTS_KEPT_OUT, "User-agent: *\nDisallow: /\n");
  for (const line of ["Disallow: /api/", "Disallow: /mcp", "Disallow: /fr/search.html", "Disallow: /en/search.html", "Disallow: /fr/export.html", "Disallow: /en/export.html", `Sitemap: ${ORIGIN}/sitemap.xml`]) {
    assert.ok(ROBOTS_INDEXED.split("\n").includes(line), line);
  }
  // No page of a law, nor a home page, is disallowed: a crawler reads them, and their own robots element.
  const disallowed = ROBOTS_INDEXED.split("\n").filter((line) => line.startsWith("Disallow: ")).map((line) => line.slice("Disallow: ".length));
  for (const path of ["/fr/", "/en/lu-legilux/loi-1991-08-10-n3", "/fr/eu-eurlex/32016R0679/fra", "/en/coverage.html", "/fr/reading.html"]) {
    assert.ok(!disallowed.some((prefix) => path.startsWith(prefix)), `${path} is allowed`);
  }
});

test("the indexing record is read whole or refused whole, as the shared table says, and names no more than a sitemap holds", () => {
  assert.ok(ENGINES.admitted.length >= 3 && ENGINES.refused.length >= 10);
  for (const { what, record } of ENGINES.admitted) assert.notEqual(readIndexingRecord(JSON.stringify(record)), null, what);
  for (const { what, record } of ENGINES.refused) assert.equal(readIndexingRecord(JSON.stringify(record)), null, what);
  for (const text of ["", "not json", "null", "42", "\"lex-v3-indexing-record/1\""]) assert.equal(readIndexingRecord(text), null, JSON.stringify(text));
  assert.notEqual(readIndexingRecord(`\uFEFF${JSON.stringify(ENGINES.record)}`), null, "a byte-order mark is no part of it");
  const record = readIndexingRecord(JSON.stringify(ENGINES.record));
  assert.equal(record.builtAt, ENGINES.record.built_at);
  assert.deepEqual(record.works.map((work) => work.address), ENGINES.record.works.map((work) => work.address));
  // A record whose sitemap would hold more addresses than one file may is refused, not cut.
  const perWork = 2 * (1 + 1);
  const many = Math.floor((SITEMAP_MAXIMUM_URLS - 2 * 2) / perWork) + 1;
  const works = Array.from({ length: many }, (_, index) => ({ address: `/lu-legilux/w${String(index).padStart(6, "0")}`, languages: ["fra"] }));
  assert.equal(readIndexingRecord(JSON.stringify({ schema: ENGINES.record.schema, built_at: null, works })), null, "over the sitemap's limit");
  assert.notEqual(readIndexingRecord(JSON.stringify({ schema: ENGINES.record.schema, built_at: null, works: works.slice(1) })), null, "at it");
});

test("the sitemap lists the home pages, Trust and Coverage, and each listed work's dossier and readable addresses in both languages, with their alternates, never a permalink (R67)", () => {
  const record = readIndexingRecord(JSON.stringify(ENGINES.record));
  assert.equal(sitemapXml(record), ENGINES.sitemap);
  assert.equal(sitemapXml(null), ENGINES.sitemapWithoutRecord);
  const locs = [...ENGINES.sitemap.matchAll(/<loc>([^<]*)<\/loc>/g)].map((match) => match[1]);
  assert.equal(locs.length, 2 * (2 + ENGINES.record.works.reduce((count, work) => count + 1 + work.languages.length, 0)));
  assert.equal(new Set(locs).size, locs.length, "each address once");
  for (const loc of locs) {
    assert.ok(loc.startsWith(`${ORIGIN}/fr/`) || loc.startsWith(`${ORIGIN}/en/`), `${loc}: under an interface language, a page that answers itself`);
    const named = parseSitePath(loc.slice(ORIGIN.length));
    if (named !== null) assert.ok(named.kind === "work" || (named.kind === "current" && named.anchor === null), `${loc}: a dossier or an act's readable address, never a permalink`);
  }
  for (const work of ENGINES.record.works) {
    assert.ok(locs.includes(`${ORIGIN}/fr${work.address}`) && locs.includes(`${ORIGIN}/en${work.address}`), `${work.address}: its dossier`);
    for (const language of work.languages) assert.ok(locs.includes(`${ORIGIN}/en${work.address}/${language}`), `${work.address}: its readable address in ${language}`);
  }
  // Each entry names its page in each interface language and, for any other, the address with no interface language.
  for (const entry of ENGINES.sitemap.split("\n").filter((line) => line.startsWith("<url>"))) {
    const loc = /<loc>([^<]*)<\/loc>/.exec(entry)[1];
    const links = [...entry.matchAll(/<xhtml:link rel="alternate" hreflang="([^"]*)" href="([^"]*)"\/>/g)].map(([, hreflang, href]) => [hreflang, href]);
    assert.deepEqual(links.map(([hreflang]) => hreflang), ["fr", "en", "x-default"], loc);
    assert.ok(links.some(([, href]) => href === loc), `${loc}: names itself among its alternates`);
    assert.match(entry, /<lastmod>2026-10-13T21:40:36Z<\/lastmod>/, `${loc}: lastmod is the build the record read, never a version's date`);
  }
  assert.ok(!ENGINES.sitemapWithoutRecord.includes("lu-legilux") && !ENGINES.sitemapWithoutRecord.includes("eu-eurlex"), "no record: no law's page");
  assert.ok(!ENGINES.sitemapWithoutRecord.includes("<lastmod>"), "and no build to date it by");
});

test("a page with the kept-out marker is opened only with the site indexed, and a law's page only for a work the record lists (R92), as the shared table says", () => {
  const record = readIndexingRecord(JSON.stringify(ENGINES.record));
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
  for (const [what, html] of Object.entries(never)) assert.deepEqual(robotsMetas(html), [NEVER_INDEXED_META], what);
  const opened = {
    home: renderLiveHomePage(),
    coverage: renderLiveCoveragePage({ card: CENSUS_EVALUATION_CARD }),
    "a dossier": renderLiveDossierPage({ initial: { envelope: DOSSIER, path: "/en/lu-legilux/loi-1991-08-10-n3" } }),
    "a readable address": renderLiveReadingPage({ initial: { envelope: READING, path: "/en/lu-legilux/loi-1991-08-10-n3/fra/art_15" } }),
    "a permalink of today's version": renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}/art_15` } }),
  };
  for (const [what, html] of Object.entries(opened)) assert.deepEqual(robotsMetas(html), [KEPT_OUT_META], what);
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

async function serve(options) {
  const server = createLiveServer({ apiOrigin: "http://127.0.0.1:9", ...options });
  const origin = await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
  return { origin, close: () => new Promise((resolve) => server.close(resolve)) };
}

test("the live server says the switch's state: robots.txt, the sitemap, X-Robots-Tag and the robots element, in both states, as the shared table says", async () => {
  const { root, record } = await fixtureRoot();
  const rows = TABLE.paths.filter((row) => row.searchEngines !== undefined);
  assert.deepEqual(rows.map((row) => row.path), ["/robots.txt", "/sitemap.xml"]);
  try {
    for (const [state, options] of [[KEPT_OUT, { searchEngines: undefined }], [KEPT_OUT, { searchEngines: "Indexed" }], [INDEXED, { searchEngines: "indexed", indexingRecord: record }]]) {
      const live = await serve({ root: pathToFileURL(`${root}/`), ...options });
      try {
        for (const row of rows) {
          const expected = row.searchEngines[state];
          const answer = await fetch(`${live.origin}${row.path}`);
          const body = await answer.text();
          assert.equal(answer.status, expected.status, `${state} ${row.path}`);
          if (!expected.claimed) continue;
          assert.equal(answer.headers.get("content-type"), expected.type, `${state} ${row.path}`);
          assert.equal(answer.headers.get("x-robots-tag"), X_ROBOTS_TAG, `${state} ${row.path}: no search result of its own`);
          assert.equal(body, expected.body === "robots" ? ENGINES.robots[state] : ENGINES.sitemap, `${state} ${row.path}`);
          const head = await fetch(`${live.origin}${row.path}`, { method: "HEAD" });
          assert.equal(await head.text(), "", `${state} ${row.path}: HEAD carries no body`);
        }
        const pages = [
          ["/en/", state === INDEXED],
          ["/fr/", state === INDEXED],
          ["/en/search.html", false],
          ["/en/lu-legilux/loi-1991-08-10-n3", state === INDEXED],
          ["/en/lu-legilux/loi-2017-03-14-a439", false],
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
    // Indexed with no record: the home pages are opened, no law's page, and the sitemap lists none.
    const live = await serve({ root: pathToFileURL(`${root}/`), searchEngines: "indexed" });
    try {
      assert.equal(await (await fetch(`${live.origin}/sitemap.xml`)).text(), ENGINES.sitemapWithoutRecord);
      assert.equal((await fetch(`${live.origin}/en/`)).headers.get("x-robots-tag"), null);
      assert.equal((await fetch(`${live.origin}/en/lu-legilux/loi-1991-08-10-n3`)).headers.get("x-robots-tag"), X_ROBOTS_TAG);
    } finally {
      await live.close();
    }
  } finally {
    await rm(record, { force: true });
    await rm(root, { recursive: true, force: true });
  }
});
