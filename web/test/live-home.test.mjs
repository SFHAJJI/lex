// The home page (the specification of record's R47, R52, R11, R13 and R50; the plan's items 4.1 and 6.8): the one box with
// R52's placeholder, a date field that holds today in Luxembourg and is sent with the search, what Lex is and is not, a
// coverage line whose every count is the coverage answer's, links only to pages the site serves, nothing for a feature it
// does not serve, and the sentence that says what Lex is not on the home page, the search results, Reading and the footer.
// The French of the same is held by `live-ssr.test.mjs`, which builds the French renderer.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup, renderToString } from "react-dom/server";

import {
  LIVE_CONTRACT, LIVE_HOME_ROOT, SearchAnswerView, liveHomeTree, renderLiveCoveragePage, renderLiveDossierPage, renderLiveHomePage, renderLiveNotFoundPage,
  renderLiveReadingPage, renderLiveSearchPage,
} from "../.react-build/app.mjs";
import { HOME_FACTS_ID, coverageLineOf, homeFacts, readHomeFacts, readHomeFactsValue } from "../scripts/live-home.mjs";
import { createSearchSession, searchOutcome, searchParameters, todayInLuxembourg } from "../scripts/live-search.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { coverageOutcome } from "../scripts/live-coverage.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { answerHandedWith } from "../scripts/v3-client.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const TODAY = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart = "") => structuredClone(census.envelopes.find((entry) => entry.operation === operation && entry.envelope.result !== null && entry.scenario.startsWith(scenarioStart)).envelope);
const COVERAGE = envelopeOf("coverage");
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const EN = liveChrome("en");

const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
const mainOf = (html) => html.slice(html.indexOf("<main"), html.indexOf("</main>"));
const coverageAt = (observedAt, change = (value) => value) => {
  const envelope = structuredClone(COVERAGE);
  envelope.context.freshness.observed_at = observedAt;
  envelope.result.value = change(envelope.result.value);
  return envelope;
};
const rendered = (envelope) => renderLiveHomePage({ facts: homeFacts(envelope, LIVE_CONTRACT) });

test("the home page is the one box with R52's placeholder, first in its content, and nothing for a feature not served", () => {
  for (const html of [rendered(COVERAGE), renderLiveHomePage()]) {
    const main = mainOf(html);
    assert.equal((main.match(/<input type="search"/g) ?? []).length, 1, "one box");
    assert.ok(main.includes(`placeholder="${escaped(EN.search.placeholder)}"`), "R52's placeholder");
    assert.equal(EN.search.placeholder, "Search by title, number, ELI or CELEX, or words from the text", "word for word R52's");
    assert.ok(main.indexOf('<input type="search"') < main.indexOf("<input", main.indexOf('<input type="search"') + 1), "the box is the first field");
    assert.equal(main.search(/<(input|select|textarea|button)\b/), main.indexOf('<input type="search"'), "and the first control of the page's content");
    assert.ok(!main.includes(escaped(EN.form.workIdentifierOptional)), "no work identifier: the home page's search is the one box");
    // R52: no assistant, no chips, no toggles, no basket, no translation toggle.
    assert.doesNotMatch(main, /aria-pressed|type="checkbox"|type="radio"|role="switch"/, "no chip and no toggle");
    assert.doesNotMatch(main, /assistant|\bAsk\b|basket|translat/i, "no assistant, basket or translation control");
    assert.doesNotMatch(main, /data-example/, "no example search");
  }
});

test("the home page says what Lex is and is not, and the sentence under the box is R50's, word for word", () => {
  assert.equal(EN.site.notAdvice, "Lex shows the published text of the law. It does not tell you whether or how it applies to your situation and is not legal advice.");
  assert.equal(liveChrome("fr").site.notAdvice, "Lex montre le texte de la loi. Il ne dit pas ce qui s’applique à votre situation, et ce n’est pas un conseil juridique.", "J6's French, with the typographic apostrophe");
  const main = mainOf(rendered(COVERAGE));
  const statement = `<p class="not-advice" data-not-advice="">${escaped(EN.site.notAdvice)}</p>`;
  assert.ok(main.includes(statement), "under the box");
  assert.ok(main.indexOf("</form>") < main.indexOf(statement) && main.indexOf(statement) < main.indexOf("data-live-answer"), "right after the form, before any answer");
  assert.ok(main.includes(`<h2>${escaped(EN.home.aboutHeading)}</h2><p>${escaped(EN.home.intro)}</p>`), "what Lex is and is not");
});

test("the home page links to Changes and to Trust and Coverage, and to no page the site does not serve", () => {
  for (const html of [rendered(COVERAGE), renderLiveHomePage()]) {
    const links = /<nav aria-label="([^"]*)" class="home-links" data-home-links="">([\s\S]*?)<\/nav>/.exec(html);
    assert.ok(links !== null, "the links");
    assert.equal(links[1], escaped(EN.home.links));
    assert.deepEqual([...links[2].matchAll(/<a href="([^"]+)">([^<]+)<\/a>/g)].map(([, href, text]) => [href, text]), [
      ["/en/radar.html", EN.site.radar],
      ["/en/coverage.html", EN.coverage.title],
    ]);
    // Developers and Get help have no live page yet (R52: nothing unserved is shown).
    assert.doesNotMatch(mainOf(html), /developer|get-help|help\.html/i);
  }
});

test("today in Luxembourg fills the date field, by the shared table's rule, around each clock change, never the build's time", () => {
  const changes = TODAY.instants.filter((row) => /summer time (starts|ends)/.test(row.case));
  assert.ok(changes.length >= 8, "the instants around the clock changes");
  for (const row of changes) {
    // The server's render: today when the API answered (its clock, in the answer's seven-digit shape).
    const answeredAt = row.instant.includes(".") ? row.instant : row.instant.replace(/Z$/, ".0000000Z");
    const facts = homeFacts(coverageAt(answeredAt), LIVE_CONTRACT);
    assert.equal(facts.today, row.date, `${row.instant}: ${row.case}`);
    const main = mainOf(renderLiveHomePage({ facts }));
    assert.match(main, new RegExp(`data-search-date="" aria-describedby="search-today" value="${row.date}"`), `${row.instant}: the field holds it`);
    assert.ok(main.includes(`>Today in Luxembourg: <!-- -->${row.date}<!-- -->.</p>`) || main.includes(`>Today in Luxembourg: ${row.date}.</p>`), `${row.instant}: and says it`);
    // A page served as built: this browser's clock, by the same rule.
    assert.equal(todayInLuxembourg(Date.parse(row.instant.replace(/(\.\d{3})\d+Z$/, "$1Z"))), row.date, `${row.instant}: the browser's today`);
  }
  for (const row of TODAY.refused.filter((refused) => /^\d{4}-/.test(refused.instant) && Number(refused.instant.slice(0, 4)) >= 1970)) {
    assert.equal(todayInLuxembourg(Date.parse(row.instant)), null, `${row.case}: no date rather than a wrong one`);
  }
  assert.doesNotMatch(mainOf(renderLiveHomePage()), /data-search-date=""[^>]*value="\d/, "the page as built holds no date: the build's time is never today");
});

test("the date is sent with the search and printed from the answer; a blank date is no date, and a date that is no day is not sent", async () => {
  assert.deepEqual(searchParameters({ query: "loi", language: "fra", date: "2026-10-25" }), { query: "loi", language: "fra", date: "2026-10-25" });
  assert.deepEqual(searchParameters({ query: "loi", language: "fra", date: "" }), { query: "loi", language: "fra" });
  assert.deepEqual(searchParameters({ query: "loi", language: "fra", date: "  " }), { query: "loi", language: "fra" });
  assert.deepEqual(Object.keys(searchParameters({ query: "loi", language: "fra", identifier: "32016R0679", date: "2026-10-25", after: "x" })), ["query", "language", "identifier", "date", "after"]);
  for (const date of ["2026-02-30", "25/10/2026", "2026-10-25T00:00:00Z", 20261025]) {
    assert.throws(() => searchParameters({ query: "loi", language: "fra", date }), /date/, String(date));
  }
  const sent = [];
  const fetchImpl = async (url, init) => {
    sent.push(JSON.parse(init.body));
    return new Response("{}", { status: 500 });
  };
  const outcomes = [];
  const session = createSearchSession({ contract: LIVE_CONTRACT, fetchImpl, onOutcome: (outcome) => outcomes.push(outcome) });
  assert.equal(session.ask({ query: "assemblée générale", language: "fra", date: "2026-10-25" }), true);
  assert.equal(session.ask({ query: "assemblée générale", language: "fra", date: "2026-13-01" }), false, "refused before anything is sent");
  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.deepEqual(sent, [{ operation_id: "search", parameters: { query: "assemblée générale", language: "fra", date: "2026-10-25" } }]);
  assert.equal(outcomes.at(-1).state, "invalid_request");
});

test("the coverage line says only what the coverage answer counts, as of its index's build date (R11)", () => {
  const line = coverageLineOf(coverageOutcome(answerHandedWith("coverage", COVERAGE, { contract: LIVE_CONTRACT, objectType: "coverage_report" })));
  const { totals, history } = COVERAGE.result.value;
  const mounted = history.snapshots_with_text.find((snapshot) => snapshot.retained_as.includes("mounted"));
  assert.deepEqual(line, { state: "counted", works: totals.works, states: totals.states, articles: totals.articles, builtOn: mounted.built_at.slice(0, 10) });
  assert.ok(mainOf(rendered(COVERAGE)).includes(`This site holds ${totals.works} act of Luxembourg law, in ${totals.states} version with ${totals.articles} articles, as of the index built on ${line.builtOn}.`));

  // Other counts, another build: the line follows the answer, and says the build's date in Luxembourg.
  const other = coverageAt("2026-10-15T10:00:00.0000000Z", (value) => {
    value.totals = { ...value.totals, works: 1730, states: 1731, articles: 52617 };
    value.languages = value.languages.map((row) => ({ ...row, works: 1730, states: 1731, articles: 52617, articles_with_searchable_text: row.articles_with_searchable_text }));
    value.history.snapshots_with_text = value.history.snapshots_with_text.map((snapshot) => ({ ...snapshot, built_at: "2026-10-13T22:40:36Z" }));
    value.history.history_begins = "2026-10-13T22:40:36Z";
    return value;
  });
  const otherFacts = homeFacts(other, LIVE_CONTRACT);
  assert.deepEqual(otherFacts.coverage, { state: "counted", works: 1730, states: 1731, articles: 52617, builtOn: "2026-10-14" }, "22:40 UTC on 13 October is the 14th in Luxembourg");
  assert.ok(mainOf(renderLiveHomePage({ facts: otherFacts })).includes("This site holds 1730 acts of Luxembourg law, in 1731 versions with 52617 articles, as of the index built on 2026-10-14."));

  // A log that records no build: the counts, and no date.
  const undated = homeFacts(coverageAt("2026-10-15T10:00:00.0000000Z", (value) => ({
    ...value,
    history: { log_records_builds: false, snapshots_in_log: 0, history_begins: null, retention_policy: null, snapshots_with_text: [], snapshots_without_text: 0, note: value.history.note },
  })), LIVE_CONTRACT);
  assert.equal(undated.coverage.builtOn, null);
  assert.ok(mainOf(renderLiveHomePage({ facts: undated })).includes(`; the index it holds records no build date.`));

  // No corpus mounted: the coverage page's own sentence; an answer that cannot be read: no line.
  const refused = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.refusal?.code === "no_corpus_mounted")?.envelope;
  assert.ok(refused, "the census holds coverage's no-mount refusal");
  const unmounted = homeFacts(refused, LIVE_CONTRACT);
  assert.deepEqual(unmounted.coverage, { state: "unmounted" });
  assert.ok(mainOf(renderLiveHomePage({ facts: unmounted })).includes('data-coverage-line="unmounted">This build has no index mounted.</p>'));
  const unreadable = homeFacts(coverageAt("2026-10-15T10:00:00.0000000Z", (value) => ({ ...value, totals: { ...value.totals, works: -1 } })), LIVE_CONTRACT);
  assert.deepEqual(unreadable.coverage, { state: "unavailable" });
  assert.doesNotMatch(mainOf(renderLiveHomePage({ facts: unreadable })), /data-coverage-line/);
});

test("the facts a home page carries are the facts it was rendered from, read back whole, and nothing else is read", () => {
  const facts = homeFacts(COVERAGE, LIVE_CONTRACT);
  const html = renderLiveHomePage({ facts });
  const open = `<script type="application/json" id="${HOME_FACTS_ID}">`;
  const block = html.slice(html.indexOf(open) + open.length, html.indexOf("</script>", html.indexOf(open)));
  assert.deepEqual(JSON.parse(block), facts);
  assert.deepEqual(readHomeFacts({ getElementById: (id) => (id === HOME_FACTS_ID ? { textContent: block } : null) }), facts);
  assert.equal(readHomeFacts({ getElementById: () => null }), null, "the page as built carries none");
  assert.ok(!renderLiveHomePage().includes(HOME_FACTS_ID));
  for (const bad of [
    null, [], { today: "2026-02-30", coverage: { state: "unmounted" } }, { today: null, coverage: { state: "counted", works: 1, states: 1, articles: -1, builtOn: null } },
    { today: null, coverage: { state: "counted", works: 1, states: 1, articles: 1, builtOn: "yesterday" } }, { today: null, coverage: { state: "guessed" } },
  ]) assert.equal(readHomeFactsValue(bad), null, JSON.stringify(bad));
  // The tree the browser hydrates is the tree the server rendered.
  const root = `<div id="${LIVE_HOME_ROOT}">`;
  const start = html.indexOf(root) + root.length;
  assert.equal(html.slice(start, html.lastIndexOf("</div>", html.indexOf(open))), renderToString(liveHomeTree(facts)), "hydration changes nothing");
  // The facts are a few values, never the coverage answer's capability cells.
  assert.ok(block.length < 200, `${block.length} bytes`);
});

test("R50's sentence is on the home page, the search results, Reading and the footer of every page", () => {
  const said = `data-not-advice="">${escaped(EN.site.notAdvice)}</p>`;
  const permalink = READING.result.value.states[0].permalink;
  const pages = {
    home: rendered(COVERAGE),
    "home as built": renderLiveHomePage(),
    search: renderLiveSearchPage(),
    reading: renderLiveReadingPage(),
    "reading at a permalink": renderLiveReadingPage({ initial: { envelope: READING, path: `/en${permalink}` } }),
    "the 404 page": renderLiveNotFoundPage(),
    coverage: renderLiveCoveragePage(),
    dossier: renderLiveDossierPage(),
  };
  for (const [name, html] of Object.entries(pages)) {
    const footer = html.slice(html.indexOf("<footer"), html.indexOf("</footer>"));
    assert.ok(footer.startsWith('<footer class="site-footer"><p data-not-advice="">'), `${name}: the footer says it first`);
    assert.ok(footer.includes(escaped(EN.site.authenticity)), `${name}: and which texts are authentic`);
    assert.ok(!html.includes("Documentation, not legal advice"), `${name}: the line it replaced is gone`);
  }
  for (const name of ["home", "home as built", "search", "reading", "reading at a permalink", "the 404 page"]) {
    const main = mainOf(pages[name]);
    assert.ok(main.includes(said), `${name}: above the answer`);
    assert.ok(main.indexOf(said) < main.indexOf("data-live-answer"), `${name}: before the results or the text`);
  }
});

test("the search page keeps its work identifier and an empty date, and the 404 page offers the home page's form", () => {
  const search = mainOf(renderLiveSearchPage());
  assert.ok(search.includes(escaped(EN.form.workIdentifierOptional)), "the work identifier, which is how an EU act is searched");
  assert.match(search, /data-search-date="" value=""/, "no date until the reader writes one: an EU act is searched with none");
  assert.ok(search.includes(`placeholder="${escaped(EN.search.placeholder)}"`));
  const notFound = mainOf(renderLiveNotFoundPage());
  assert.ok(notFound.includes('data-variant="home"'), "the 404 page's search is the home page's form");
  assert.ok(!notFound.includes(escaped(EN.form.workIdentifierOptional)));
});

/** A Luxembourg search answer from the census, asked at `date` (or none), as the results lay it out. */
function searchedAt(date, change = (view) => view) {
  const envelope = envelopeOf("search", "a phrase the held text carries");
  envelope.result.value.requested_date = date;
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  return renderToStaticMarkup(h(SearchAnswerView, { outcome: { ...outcome, view: change(outcome.view) }, onNextPage: () => {} })).replaceAll("<!-- -->", "");
}

test("a dated search says what this site holds was searched, in Luxembourg law, never that a version applies (review of #971)", () => {
  const html = searchedAt("2026-10-16");
  const dated = EN.search.dated.replace("{date}", "2026-10-16");
  assert.ok(html.includes(`data-searched-on="2026-10-16">${escaped(dated)}</p>`), html);
  assert.equal(EN.search.dated, "Only Luxembourg acts were searched. For each, the latest version this site holds that is applicable from {date} or an earlier date (publisher) was searched, and an act with no such version was not. An act held only as published is searched as published, and may have been amended since.");
  assert.equal(liveChrome("fr").search.dated, "Seuls des actes luxembourgeois ont été interrogés. Pour chacun, la dernière version que ce site détient qui est applicable à partir du {date} ou d’une date antérieure (éditeur) a été interrogée, et un acte sans une telle version ne l’a pas été. Un acte détenu uniquement tel que publié est interrogé tel que publié, et peut avoir été modifié depuis.");
  // The latest held version on or before the date (SelectAtDate), which for an act held only as published is that act as
  // published: never "the version that applies", which on a population of acts held as first published claims too much.
  assert.doesNotMatch(dated, /\bthat applies\b|\bapplying on\b|\bin force\b/);
  for (const language of ["en", "fr"]) {
    for (const entry of ["dated", "undated", "noHitLuxembourg", "luxembourgOnly"]) assertTimeVocabulary(liveChrome(language).search[entry], `${language} search.${entry}`);
    // The as-published label's own words (common.asPublished.label): "as published" and "may have been amended since".
    const label = liveChrome(language).common.asPublished.label.split(";").map((part) => part.trim());
    for (const words of label.slice(0, 2)) assert.ok(liveChrome(language).search.dated.includes(words), `${language}: the dated sentence says "${words}"`);
  }
  const undated = searchedAt(null);
  assert.ok(undated.includes(`data-searched-on="">${escaped(EN.search.undated)}</p>`));
  assert.match(EN.search.undated, /^Only Luxembourg acts were searched/);
});

test("with no work named, the no-hit sentence says the search was of Luxembourg law; with one named, it names no publisher (review of #971)", () => {
  const unnamed = searchedAt(null, (view) => ({ ...view, hits: [], identifier: null }));
  assert.ok(unnamed.includes("No article of the Luxembourg text this server holds and may search contains “assemblée générale”."), unnamed);
  const named = searchedAt(null, (view) => ({ ...view, hits: [], identifier: "/lu-legilux/loi-1991-08-10-n3" }));
  assert.ok(named.includes("No article of the text this server holds and may search contains “assemblée générale”."), named);
});

test("under the home page's box and the 404 page's, a line says the search covers Luxembourg law and leads to the search page for an EU act (review of #971)", () => {
  const line = (html) => /<p class="search-scope" data-search-scope="">([\s\S]*?)<\/p>/.exec(mainOf(html).replaceAll("<!-- -->", ""))?.[1] ?? null;
  for (const html of [rendered(COVERAGE), renderLiveHomePage(), renderLiveNotFoundPage()]) {
    assert.equal(line(html), 'This search covers Luxembourg law. To search within an EU act, name it by its CELEX number on the <a href="/en/search.html">search page</a>.');
    const main = mainOf(html);
    assert.ok(main.indexOf("data-not-advice") < main.indexOf("data-search-scope") && main.indexOf("data-search-scope") < main.indexOf("data-live-answer"), "under R50's sentence, so the phone's first screen keeps the box and that sentence");
  }
  assert.equal(line(renderLiveSearchPage()), null, "the search page names a work itself");
  assert.equal(liveChrome("fr").search.luxembourgOnly, "Cette recherche porte sur le droit luxembourgeois. Pour interroger un acte de l’UE, désignez-le par son numéro CELEX sur la {link}.");
  assert.equal(liveChrome("fr").search.searchPage, "page de recherche");
});
