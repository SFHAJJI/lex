// No word a reader types reaches an address a server could record (the specification of record's R109: "The site keeps
// search state in the URL fragment, so a search can be linked, and sends the query in a request body"; the plan's item 3.5;
// the launch contract's "no query text, IP or user agent recorded").
//
// Three holds:
// - every place the code a page runs in the browser could send a request or change an address is found by a scan of the
//   files each page's script is built from (its import closure), and each one is pinned here, line for line, with why it
//   carries no word: a request goes to `/api/v3/{operation}` with the words in its body (`v3-client.mjs`); the page's own
//   address changes only by `replacePageAddress`, which writes a search after "#" or a date into the query; a reference
//   that opens its act goes to the act's address. A new builder fails here until it is pinned with its reason;
// - every live screen, asked with words, asks exactly `POST /api/v3/{operation}`, no query and no fragment, the words in
//   the body alone;
// - every page's forms carry no `action` and their controls no `name`, so a submit the browser performs itself (before the
//   script, or without it) sends nothing, the 404 page's box included, and that box keeps nothing in the address.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  LIVE_CONTRACT, renderLiveComparePage, renderLiveCoveragePage, renderLiveDossierPage, renderLiveExportPage, renderLiveHistoryPage,
  renderLiveHomePage, renderLiveNotFoundPage, renderLiveRadarPage, renderLiveReadingPage, renderLiveSearchPage,
} from "../.react-build/app.mjs";
import { createSearchSession } from "../scripts/live-search.mjs";
import { createReadingSession } from "../scripts/live-reading.mjs";
import { createDossierSession } from "../scripts/live-dossier.mjs";
import { createHistorySession } from "../scripts/live-history.mjs";
import { createCompareSession } from "../scripts/live-compare.mjs";
import { createRadarSession } from "../scripts/live-radar.mjs";
import { startLiveCoverage } from "../scripts/live-coverage.mjs";
import { readingPageAddress, withDateQuery } from "../scripts/permalinks.mjs";
import { searchFragment } from "../scripts/search-address.mjs";

const WEB = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** The browser entries the build bundles (`build-live.mjs`), whose import closures are the code pages run. */
const ENTRIES = [
  "client-live-home-entry.jsx", "client-live-entry.jsx", "client-live-search-entry.jsx", "client-live-dossier-entry.jsx",
  "client-live-reading-entry.jsx", "client-live-history-entry.jsx", "client-live-compare-entry.jsx", "client-live-radar-entry.jsx",
  "client-live-export-entry.jsx",
].map((name) => join(WEB, "app", name));

/** The web's own files an entry imports, transitively (packages are not the site's code). */
function closure(entries) {
  const seen = new Set();
  const pending = [...entries];
  while (pending.length > 0) {
    const file = pending.pop();
    if (seen.has(file)) continue;
    seen.add(file);
    const source = readFileSync(file, "utf8");
    for (const match of source.matchAll(/(?:^|[\s;])(?:import|export)\s[^'"]*?from\s*['"](\.{1,2}\/[^'"]+)['"]|(?:^|[\s;])import\s*['"](\.{1,2}\/[^'"]+)['"]/gm)) {
      pending.push(resolve(dirname(file), match[1] ?? match[2]));
    }
  }
  return [...seen].sort();
}

/**
 * The places code can send a request, or change the address the page stands at or goes to. A comment is not code: lines
 * that start a comment are left out, and a sink named in prose without its call is not one.
 */
const SINKS = Object.freeze([
  ["fetch", /\bfetch(?:Impl)?\s*\(/],
  ["XMLHttpRequest", /\bXMLHttpRequest\b/],
  ["sendBeacon", /\bsendBeacon\b/],
  ["EventSource", /\bEventSource\b/],
  ["WebSocket", /\bWebSocket\b/],
  ["an image's address", /\bnew\s+Image\s*\(|\.src\s*=/],
  ["a dynamic import", /\bimport\s*\(/],
  ["a navigation", /\blocation\.(?:assign|replace)\s*\(|\blocation(?:\.href)?\s*=[^=]|\bwindow\.open\s*\(/],
  ["the history", /\bhistory\.(?:pushState|replaceState)\s*\(/],
  ["the page's address", /\breplacePageAddress\s*\(/],
  ["a form's own submit", /\.(?:submit|requestSubmit)\s*\(|\b(?:action|formAction)=/],
]);

function sinksIn(file) {
  const found = [];
  readFileSync(file, "utf8").split("\n").forEach((line, index) => {
    const code = line.trim();
    if (code.startsWith("//") || code.startsWith("*") || code.startsWith("/*")) return;
    for (const [kind, pattern] of SINKS) {
      if (pattern.test(line)) found.push(`${relative(WEB, file).replaceAll("\\", "/")}: ${kind}: ${code}`);
    }
  });
  return found;
}

/** Every sink the pages' code holds, each with why it carries no word into an address. */
const PINNED = Object.freeze({
  // The one way a page asks the API: a fixed path, an operation from a closed list (`SERVED_OPERATIONS`), the parameters in
  // the body (`JSON.stringify({ operation_id, parameters })`), POST, no credentials, no referrer.
  "scripts/v3-client.mjs: fetch: response = await fetchImpl(`/api/v3/${operation}`, {": "the API, words in the body",
  // A reference that opens its act (the plan's item 4.2) goes to the act's address the platform minted (`permalinkHref`),
  // only when the reader asked it on this page.
  "app/LiveSearch.jsx: a navigation: if (outcome.state === 'opens' && submitted.current) window.location.assign(outcome.href);": "the act's own address",
  // The page's own address, replaced: the path kept, a search after "#" (never sent) or a date in the query.
  "app/page-address.mjs: the history: if (after !== before) window.history.replaceState(window.history.state, '', after);": "the page's own path, a fragment or a date",
  "app/page-address.mjs: the page's address: export function replacePageAddress(window, { search = window.location.search, hash = window.location.hash } = {}) {": "its definition: the path is the page's own",
  "app/LiveSearch.jsx: the page's address: if (fragment !== null) replacePageAddress(window, { hash: fragment });": "the search, after #",
  "app/LiveReading.jsx: the page's address: replacePageAddress(window, { search: withDateQuery(window.location.search, pin.request.date) });": "the date read at, in the query",
});

test("every place the pages' code could send a request or change an address is pinned, with why it carries no word", () => {
  const files = closure(ENTRIES);
  assert.ok(files.length > 40, `the closure is read (${files.length} files)`);
  for (const must of ["app/LiveSearch.jsx", "app/page-address.mjs", "scripts/v3-client.mjs", "scripts/search-address.mjs"]) {
    assert.ok(files.some((file) => relative(WEB, file).replaceAll("\\", "/") === must), `${must} is in a page's code`);
  }
  const found = files.flatMap(sinksIn);
  assert.deepEqual(found.sort(), Object.keys(PINNED).sort());
});

test("the scan finds each kind of sink, so a new one cannot pass it unseen", () => {
  const probe = (line) => SINKS.filter(([, pattern]) => pattern.test(line)).map(([kind]) => kind);
  assert.deepEqual(probe("fetch(`/search?q=${query}`)"), ["fetch"]);
  assert.deepEqual(probe("navigator.sendBeacon('/x', query)"), ["sendBeacon"]);
  assert.deepEqual(probe("new XMLHttpRequest()"), ["XMLHttpRequest"]);
  assert.deepEqual(probe("img.src = `/pixel?q=${query}`"), ["an image's address"]);
  assert.deepEqual(probe("window.location.href = `/search.html?q=${query}`"), ["a navigation"]);
  assert.deepEqual(probe("location = '/x'"), ["a navigation"]);
  assert.deepEqual(probe("history.pushState(null, '', `?q=${query}`)"), ["the history"]);
  assert.deepEqual(probe("<form action=\"/search\">"), ["a form's own submit"]);
  assert.deepEqual(probe("if (location.href === other) return;"), [], "a comparison is no navigation");
});

const WORDS = "zéphyrine congé parental";
const recorded = [];
const recordingFetch = async (url, init) => {
  recorded.push({ url: String(url), init });
  return new Response(JSON.stringify({}), { status: 200, headers: { "content-type": "application/json" } });
};

async function asked(start) {
  recorded.length = 0;
  await new Promise((settle) => {
    start((outcome) => {
      if (outcome.state !== "loading") settle();
    });
  });
  return recorded.slice();
}

test("every live screen asked with words asks POST /api/v3/{operation}, no query and no fragment, the words in the body alone", async () => {
  const contract = LIVE_CONTRACT;
  const screens = [
    ["search", (onOutcome) => createSearchSession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ query: WORDS, language: "fra", identifier: "", date: "2026-10-18" })],
    ["search", (onOutcome) => createSearchSession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ query: WORDS, language: "fra", identifier: WORDS, date: "" })],
    ["evidence_bundle", (onOutcome) => createReadingSession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ identifier: WORDS, date: "2026-10-18", language: "" })],
    ["dossier", (onOutcome) => createDossierSession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ identifier: WORDS, language: "" })],
    ["article_history", (onOutcome) => createHistorySession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ identifier: WORDS, anchor: WORDS, language: "" })],
    ["diff", (onOutcome) => createCompareSession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ identifier: WORDS, dateFrom: "2024-01-01", dateTo: "2026-10-18", language: "" })],
    ["changes_in_period", (onOutcome) => createRadarSession({ contract, fetchImpl: recordingFetch, onOutcome }).ask({ dateFrom: "2024-01-01", dateTo: "2026-10-18", identifier: WORDS, language: "" })],
    ["coverage", (onOutcome) => startLiveCoverage({ contract, fetchImpl: recordingFetch, onOutcome })],
  ];
  for (const [operation, start] of screens) {
    const requests = await asked(start);
    assert.equal(requests.length, 1, `${operation}: one request`);
    const [{ url, init }] = requests;
    assert.equal(url, `/api/v3/${operation}`, `${operation}: the fixed path, no query, no fragment`);
    assert.ok(!url.includes("?") && !url.includes("#") && !url.includes("z%C3%A9phyrine") && !url.includes("zéphyrine"), url);
    assert.equal(init.method, "POST");
    assert.equal(init.referrerPolicy, "no-referrer");
    assert.equal(init.credentials, "omit");
    if (operation !== "coverage") assert.ok(init.body.includes("zéphyrine"), `${operation}: the words travel in the body`);
  }
});

test("what the pages write into their own address holds no word but after #: a date and a page number in the query", () => {
  for (const search of ["", "?page=2", "?from=card&page=3", "?date=garbage", "?date=2020-01-01&date=2021-01-01"]) {
    const query = withDateQuery(search, "2026-10-18");
    assert.match(query, /(?:^\?|&)date=2026-10-18$/, query);
    assert.equal((query.match(/date=/g) ?? []).length, 1, `${query}: one date`);
  }
  assert.equal(withDateQuery("?from=card&page=3&date=x", "2026-10-18"), "?from=card&page=3&date=2026-10-18", "what the address held is kept, the date replaced");
  assert.throws(() => withDateQuery("", WORDS), /not a calendar date/);
  assert.equal(readingPageAddress("/fr/lu-legilux/x/fra", 3, "2024-01-01"), "/fr/lu-legilux/x/fra?page=3&date=2024-01-01");
  assert.equal(readingPageAddress("/fr/lu-legilux/x/fra", 1, "2024-01-01"), "/fr/lu-legilux/x/fra?date=2024-01-01");
  assert.equal(readingPageAddress("/fr/lu-legilux/x/fra", 1, null), "/fr/lu-legilux/x/fra");
  assert.equal(readingPageAddress("/fr/lu-legilux/x/fra", 2, WORDS), "/fr/lu-legilux/x/fra?page=2", "a word is no date");
  // The search goes after "#", where no server receives it.
  const fragment = searchFragment({ query: WORDS, language: "fra", date: "2026-10-18" });
  assert.ok(fragment.startsWith("#") && !fragment.includes("?"), fragment);
});

test("no page's form carries an action or a method, no control a name, and the 404 page's box keeps nothing in the address", () => {
  const pages = {
    home: renderLiveHomePage(), search: renderLiveSearchPage(), coverage: renderLiveCoveragePage(), dossier: renderLiveDossierPage(),
    reading: renderLiveReadingPage(), history: renderLiveHistoryPage(), compare: renderLiveComparePage(), radar: renderLiveRadarPage(),
    export: renderLiveExportPage(), "404": renderLiveNotFoundPage(),
  };
  for (const [name, html] of Object.entries(pages)) {
    for (const form of html.match(/<form\b[^>]*>/g) ?? []) assert.doesNotMatch(form, /\s(?:action|method)=/, `${name}: ${form}`);
    for (const control of html.match(/<(?:input|select|textarea|button)\b[^>]*>/g) ?? []) {
      assert.doesNotMatch(control, /\sname=/, `${name}: ${control}`);
      assert.doesNotMatch(control, /\sform(?:action|method)=/i, `${name}: ${control}`);
    }
  }
  assert.match(pages["404"], /id="live-search-root" data-variant="home" data-address="none"/, "the 404 page's box keeps no search in its address");
  assert.match(pages.search, /id="live-search-root" data-variant="search" data-address="kept"/);
});
