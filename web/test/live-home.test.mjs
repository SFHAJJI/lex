// The home page: since the owner's brief of 2026-11-15 the V4 Home (the mockup V1Home's left column): the headline, one
// find field that takes the words to the search page after "#" (R109), six starting texts each drawn on one scale from the
// release's record of their timelines, the newest changes of wording with each text's own title, and the coverage line whose
// counts are the coverage answer's (R11). Its facts and the deploy probe's hold are as before. The search results page (V4)
// is held here too: an exact reference opens the text page, other words list the texts by title. The French of the same is
// held by `live-ssr.test.mjs`, which builds the French renderer.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { SEARCH_EXACT_REFERENCE_2026_11_15 } from "../scripts/search-exact-reference-record.mjs";
import { homePageParts, withoutFreshness } from "../scripts/deploy-probe.mjs";
import { createElement as h } from "react";
import { renderToStaticMarkup, renderToString } from "react-dom/server";

import {
  LIVE_CONTRACT, LIVE_HOME_ROOT, RecentChanges, SearchAnswerView, changeCounts, liveHomeTree, liveSearchTree, renderLiveCoveragePage, renderLiveDossierPage, renderLiveHomePage, renderLiveNotFoundPage,
  renderLiveReadingPage, renderLiveSearchPage,
} from "../.react-build/app.mjs";
import {
  HOME_FACTS_ID, coverageLineOf, homeFacts, homeTextOfTimeline, numericDate, readHomeFacts, readHomeFactsValue, recentChangesOf, recentTitleOf,
  scaleX, startRecentChanges,
} from "../scripts/live-home.mjs";
import { HOME_TEXTS_RECORD } from "../scripts/home-texts-record.mjs";
import { findRows, openedHref } from "../scripts/find-results.mjs";
import { textHref } from "../scripts/text-address.mjs";
import { createSearchSession, searchOutcome, searchParameters, todayInLuxembourg } from "../scripts/live-search.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { coverageOutcome } from "../scripts/live-coverage.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { answerHandedWith } from "../scripts/v3-client.mjs";

const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const TODAY = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart = "") => structuredClone(census.envelopes.find((entry) => entry.operation === operation && entry.envelope.result !== null && entry.scenario.startsWith(scenarioStart)).envelope);
const COVERAGE = envelopeOf("coverage");
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const EN = liveChrome("en");
const EXACT_REFERENCE = SEARCH_EXACT_REFERENCE_2026_11_15;

const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
const mainOf = (html) => html.slice(html.indexOf("<main"), html.indexOf("</main>"));
const coverageAt = (observedAt, change = (value) => value) => {
  const envelope = structuredClone(COVERAGE);
  envelope.context.freshness.observed_at = observedAt;
  envelope.result.value = change(envelope.result.value);
  return envelope;
};
const rendered = (envelope) => renderLiveHomePage({ facts: homeFacts(envelope, LIVE_CONTRACT) });

test("the V4 Home: the headline, one find field with no action and no name, the six starting texts, the newest changes, the coverage line", () => {
  for (const html of [rendered(COVERAGE), renderLiveHomePage()]) {
    const main = mainOf(html).replaceAll("<!-- -->", "");
    assert.ok(html.includes('data-layout="v4"') && html.includes('<aside class="lx-dock"'), "the V4 frame, with the dock beside the column");
    assert.ok(main.includes(`<h1 class="lx-home-headline">${escaped(EN.home.headline)}</h1><p class="lx-home-lede">${escaped(EN.home.lede)}</p>`), "the headline and its sentence");
    // One field (R109): no action, no method, no name; the script takes the words to the search page after "#".
    const form = /<form class="lx-home-find" role="search" data-home-find="">([\s\S]*?)<\/form>/.exec(main);
    assert.ok(form !== null, "the find field's form, with no action and no method");
    assert.match(form[1], /<input id="lx-home-find-q" type="text"[^>]*placeholder="[^"]+"/);
    assert.doesNotMatch(form[1], /\sname=/, "no control a name");
    assert.ok(form[1].includes(`<button type="submit">${escaped(EN.home.findSubmit)}</button>`));
    // The examples, what the assistant does and does not, and the questions are the dock's (the brief), not the column's.
    assert.doesNotMatch(main, /data-home-examples|data-home-ask|data-home-links|data-home-trust|data-not-advice|data-search-scope/);
    assert.doesNotMatch(main.replace(/<script[\s\S]*?<\/script>/g, "").replace(/<[^>]+>/g, " "), /\/lu-legilux\/|art_\d|\b[0-9a-f]{64}\b/, "no address, article id or hash in the main flow's text");
    // The six starting texts, in the record's order, each a link to its text page.
    const rows = [...main.matchAll(/<a class="lx-home-text" href="([^"]+)" data-start-text="([^"]+)">([\s\S]*?)<\/a>/g)];
    assert.deepEqual(rows.map(([, , identifier]) => identifier), HOME_TEXTS_RECORD.texts.map((text) => text.identifier));
    for (const [, href, identifier] of rows) assert.equal(href, textHref({ identifier }).replaceAll("&", "&amp;"));
    const insurance = rows[0][3];
    assert.ok(insurance.includes(`<span lang="fr">${escaped("Loi du 27 juillet 1997 sur le contrat d'assurance")}</span>`), "the text's own title");
    assert.ok(insurance.includes('<span class="lx-home-text-origin">Luxembourg, 147 articles</span>'));
    assert.equal((insurance.match(/<rect class="lx-home-tick"/g) ?? []).length, 10, "one tick per version");
    assert.ok(insurance.includes('<rect class="lx-home-tick" x="12.8" y="3"'), "the first version, 25/12/2006, where the mockup draws it");
    assert.ok(insurance.includes('<span class="lx-home-count-cell">10 versions</span>'));
    const dora = rows.find(([, , identifier]) => identifier === "32022R2554")[3];
    assert.ok(dora.includes(`<span class="lx-home-text-title">${escaped(EN.home.startTexts["32022R2554"])}</span>`), "an EU act by its usual name");
    assert.ok(dora.includes("EU, Regulation 2022/2554, 64 articles"));
    assert.ok(dora.includes('<circle class="lx-home-one" cx="242.2" cy="11" r="4">'), "one wording held: a hollow circle, where the mockup draws it");
    assert.ok(dora.includes(`>${EN.home.startVersions.one}</span>`));
    // Changed recently: asked once loaded, so the server's render says it is being read, and links every change.
    assert.ok(main.includes(`data-recent="loading"`) && main.includes(escaped(EN.home.recentLoading)));
    assert.ok(main.includes(`<a href="/en/radar.html">${escaped(EN.home.recentAll)}</a>`));
    // The coverage line, and the coverage page.
    assert.ok(main.includes(`<a href="/en/coverage.html">${escaped(EN.home.holdsLink)}</a>`));
    assert.ok(Buffer.byteLength(main) < 40000, "a short page");
  }
});

test("the shared scale is the mockup's: 1 January 2006 to 1 January 2027 over 300 units, 2026 labelled inside it", () => {
  assert.equal(scaleX("2006-01-01"), 0);
  assert.equal(scaleX("2026-01-01"), 285.7);
  assert.equal(scaleX("2016-04-27"), 147.4, "the GDPR's original wording, as the mockup places it");
  assert.equal(scaleX("2024-06-13"), 263.5, "the AI Act's original wording (the mockup: 263.6)");
  assert.equal(scaleX("2005-12-31"), null, "before the scale: not drawn");
  assert.equal(scaleX("2027-01-01"), null, "after it: not drawn");
  assert.equal(scaleX("2026-02-30"), null);
  assert.equal(numericDate("2026-08-03"), "03/08/2026", "dd/mm/yyyy in both languages (the brief's section 9)");
});

test("a starting text's row is read from its timeline: held versions only, the article count of the version that applies today", () => {
  const luxembourg = {
    publisher: "lu-legilux", index_sha256: "a".repeat(64),
    states: [
      { applicability_date: "2006-12-25", language: "fra", text_status: "held", articles: [{}, {}] },
      { applicability_date: "2011-04-16", language: "fra", text_status: "not_available", articles: [] },
      { applicability_date: "2025-11-25", language: "fra", text_status: "held", articles: [{}, {}, {}] },
      { applicability_date: "2027-01-01", language: "fra", text_status: "held", articles: [{}, {}, {}, {}] },
    ],
  };
  assert.deepEqual(homeTextOfTimeline(luxembourg, "2026-11-15"), { versions: { fra: ["2006-12-25", "2025-11-25", "2027-01-01"] }, articles: { fra: 3 }, index_sha256: "a".repeat(64) });
  const europe = {
    publisher: "eu-eurlex", index_sha256: "b".repeat(64),
    languages: [{ language: "fra", wordings: [
      { wording_date: "2022-12-14", text_held: true, basis: "single_work", article_count: 64 },
      { wording_date: "2022-12-27", text_held: true, basis: "different_texts", article_count: null },
      { wording_date: "2023-01-01", text_held: false, basis: "text_not_held", article_count: null },
    ] }],
  };
  assert.deepEqual(homeTextOfTimeline(europe, "2026-11-15"), { versions: { fra: ["2022-12-14"] }, articles: { fra: 64 }, index_sha256: "b".repeat(64) }, "a date two different texts answer is no version a reader can open");
  assert.equal(homeTextOfTimeline({ publisher: "lu-legilux", states: [] }), null);
  // The record the page draws from is a record of that rule: six texts, each with its versions and its articles.
  assert.equal(HOME_TEXTS_RECORD.texts.length, 6);
  for (const text of HOME_TEXTS_RECORD.texts) {
    for (const [language, dates] of Object.entries(text.versions)) {
      assert.ok(dates.length > 0 && dates.every((date, index) => index === 0 || dates[index - 1] < date), `${text.identifier} ${language}: dates in order`);
      assert.ok(Number.isInteger(text.articles[language]), `${text.identifier} ${language}: its articles counted`);
    }
    assert.ok(text.publisher === "eu-eurlex" ? /^\d{4}\/\d+$/.test(text.number) : typeof text.title === "string", text.identifier);
  }
});

test("a recent change is named by the text's own title, never a modifying act's, an identifier or a date (trap 2)", () => {
  // loi-2018-07-18-a621 (the brief's trap 2): its dossier's titles start with a modifying act's.
  const police = { titles: [
    { language: "fra", short_titles: [], titles: [{ title: "Version consolidée applicable au 20/08/2018 : Loi du 29 juillet 2020 portant modification de la loi modifiée du 18 juillet 2018 sur la Police grand-ducale." }] },
    { language: "fra", short_titles: [{ title: "Loi du 18 juillet 2018 sur la Police grand-ducale." }], titles: [{ title: "Version consolidée applicable au 25/07/2026 : Loi du 18 juillet 2018 sur la Police grand-ducale\\net portant modification :\\n1° du Code de procédure pénale ;" }] },
  ] };
  assert.deepEqual(recentTitleOf(police, "loi-2018-07-18-a621"), { title: "Loi du 18 juillet 2018 sur la Police grand-ducale", language: "fra" });
  // The newest expression's title, its version prefix and its later lines removed, when it names the act's own date.
  const firstOfMonth = { titles: [{ language: "fra", short_titles: [], titles: [{ title: "Version consolidée applicable au 01/11/2026 : Règlement grand-ducal du 1er juillet 1988 concernant X.\\nY" }] }] };
  assert.deepEqual(recentTitleOf(firstOfMonth, "rgd-1988-07-01-n3"), { title: "Règlement grand-ducal du 1er juillet 1988 concernant X", language: "fra" });
  assert.equal(recentTitleOf({ titles: [{ language: "fra", short_titles: [], titles: [{ title: "Loi du 29 juillet 2020 portant modification" }] }] }, "loi-2018-07-18-a621"), null, "no own title: no row");
  assert.deepEqual(recentTitleOf({ titles: [{ language: "fra", short_titles: [], titles: [{ title: "Code de commerce" }] }] }, "code-commerce"), { title: "Code de commerce", language: "fra" }, "a code by its title");
  assert.equal(recentTitleOf({ titles: [] }, "code-commerce"), null);
});

test("Changed recently: the newest changes of wording up to today, each counted in words and linked to the comparison", async () => {
  const row = (date, workKey, changed, counts = { added: 0, changed: 1, removed: 0, unchanged: 9 }) => ({
    work_key: workKey, wording_changed: changed, state: { applicability_date: date }, diff: { date_from: "2020-01-01", date_to: date }, counts, baseline: null,
  });
  const value = { changes: [
    row("2026-10-11", "rgd-2004-03-05-n15", true), row("2026-11-01", "rgd-1988-07-15-n3", true), row("2026-11-14", "rgd-2026-07-22-a390", null, null),
    row("2026-12-01", "loi-2030-01-01-a1", true), row("2026-09-27", "code-consommation", true, { added: 1, changed: 1, removed: 0, unchanged: 3 }),
  ] };
  assert.deepEqual(recentChangesOf(value, "2026-11-15").map((change) => [change.date, change.workKey, change.from]), [
    ["2026-11-01", "rgd-1988-07-15-n3", "2020-01-01"], ["2026-10-11", "rgd-2004-03-05-n15", "2020-01-01"], ["2026-09-27", "code-consommation", "2020-01-01"],
  ], "wording changed, on or before today, newest first");
  assert.equal(changeCounts({ added: 1, changed: 1, removed: 0 }), "1 article added, 1 changed");
  assert.equal(changeCounts({ added: 0, changed: 7, removed: 0 }), "7 articles changed");
  assert.equal(changeCounts({ added: 2, changed: 0, removed: 1 }), "2 articles added, 1 removed");

  // Asked once loaded: one changes_in_period over the last weeks, then one dossier per row for its title, each in the body.
  const sent = [];
  const changes = structuredClone(census.envelopes.find((entry) => entry.operation === "changes_in_period" && !entry.envelope.refusal && entry.envelope.result.value.changes.length > 0).envelope);
  changes.result.value.changes = value.changes;
  const dossier = (title) => {
    const envelope = structuredClone(envelopeOf("dossier"));
    envelope.result.value.titles = [{ language: "fra", short_titles: [], titles: [{ title }] }];
    return envelope;
  };
  const titles = { "rgd-1988-07-15-n3": "Règlement grand-ducal du 15 juillet 1988 concernant l'amiante", "rgd-2004-03-05-n15": "Règlement grand-ducal du 5 mars 2004 X", "code-consommation": "Code de la consommation" };
  const fetchImpl = async (url, init) => {
    const body = JSON.parse(init.body);
    sent.push([url, body.operation_id, body.parameters]);
    const envelope = body.operation_id === "changes_in_period" ? changes : dossier(titles[body.parameters.identifier.split("/").at(-1)]);
    return new Response(JSON.stringify(envelope), { status: 200, headers: { "content-type": "application/json" } });
  };
  const rows = await new Promise((resolve) => startRecentChanges({ contract: LIVE_CONTRACT, fetchImpl, today: "2026-11-15", onRows: resolve }));
  assert.deepEqual(rows.map((change) => [change.date, change.title]), [
    ["2026-11-01", titles["rgd-1988-07-15-n3"]], ["2026-10-11", titles["rgd-2004-03-05-n15"]], ["2026-09-27", titles["code-consommation"]],
  ]);
  assert.deepEqual(sent.slice(0, 2).map(([url, operation, parameters]) => [url, operation, parameters]), [
    ["/api/v3/changes_in_period", "changes_in_period", { date_from: "2026-10-01", date_to: "2026-11-15", limit: 200 }],
    ["/api/v3/changes_in_period", "changes_in_period", { date_from: "2026-07-18", date_to: "2026-11-15", limit: 200 }],
  ], "a wider window only while the narrower one held too few changes");
  assert.ok(sent.length <= 3 + 5, `at most eight requests (${sent.length})`);
  // Drawn: the date dd/mm/yyyy, the title, the counts, the text page comparing the version with the one before.
  const html = renderToStaticMarkup(h(RecentChangesProbe, { rows }));
  assert.ok(html.includes('<span class="lx-home-change-date">01/11/2026</span>'));
  assert.ok(html.includes(`href="${textHref({ identifier: "/lu-legilux/rgd-1988-07-15-n3", from: "2020-01-01", to: "2026-11-01" }).replaceAll("&", "&amp;")}"`));
  assert.ok(html.includes("1 article added, 1 changed"));
});

test("the date is sent with the search and printed from the answer; a blank date is no date, and a date that is no day is not sent", async () => {
  assert.deepEqual(searchParameters({ query: "loi", language: "fra", date: "2026-10-25" }), { query: "loi", language: "fra", mode: "strict", date: "2026-10-25" });
  assert.deepEqual(searchParameters({ query: "loi", language: "fra", date: "" }), { query: "loi", language: "fra", mode: "strict" });
  assert.deepEqual(searchParameters({ query: "loi", language: "fra", date: "  " }), { query: "loi", language: "fra", mode: "strict" });
  assert.deepEqual(Object.keys(searchParameters({ query: "loi", language: "fra", identifier: "32016R0679", date: "2026-10-25", after: "x" })), ["query", "language", "mode", "identifier", "date", "after"]);
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
  assert.deepEqual(sent, [{ operation_id: "search", parameters: { query: "assemblée générale", language: "fra", mode: "strict", date: "2026-10-25" } }]);
  assert.equal(outcomes.at(-1).state, "invalid_request");
});

/**
 * The coverage answer's text yield moved with its totals, as the producer holds the build report's yield to the mount
 * (its Luxembourg articles are the index's): served articles where there are any, and none where the mount serves none,
 * its one file then counted as an admitted file with no article.
 */
function yieldServing(value, articles) {
  const publisher = value.text_yield.publishers[0];
  const row = publisher.classes[0];
  const served = articles > 0;
  for (const node of [publisher, row]) {
    node.articles = articles;
    if (!served) Object.assign(node, { files_with_text: 0, versions_with_text: 0, works_with_text: 0 });
  }
  if (!served) row.files_parse_failed = [{ reason: "akn_unsupported_content_shape", count: row.files }];
}

test("the coverage line says only what the coverage answer counts, as of its index's build date (R11)", () => {
  const line = coverageLineOf(coverageOutcome(answerHandedWith("coverage", COVERAGE, { contract: LIVE_CONTRACT, objectType: "coverage_report" })));
  const { totals, history } = COVERAGE.result.value;
  const mounted = history.snapshots_with_text.find((snapshot) => snapshot.retained_as.includes("mounted"));
  // With the digest of the index it counts, which the example searches are held to (`home-examples.mjs`).
  assert.deepEqual(line, { state: "counted", works: totals.works, states: totals.states, articles: totals.articles, builtOn: mounted.built_at.slice(0, 10), indexSha256: COVERAGE.result.value.mounted.index_sha256 });
  assert.ok(mainOf(rendered(COVERAGE)).includes(`Lex holds ${totals.works} text in ${totals.states} dated version.`));

  // Other counts, another build: the line follows the answer, and says the build's date in Luxembourg.
  const other = coverageAt("2026-10-15T10:00:00.0000000Z", (value) => {
    value.totals = { ...value.totals, works: 1730, states: 1731, articles: 52617 };
    yieldServing(value, 52617);
    value.languages = value.languages.map((row) => ({ ...row, works: 1730, states: 1731, articles: 52617, articles_with_searchable_text: row.articles_with_searchable_text }));
    value.history.snapshots_with_text = value.history.snapshots_with_text.map((snapshot) => ({ ...snapshot, built_at: "2026-10-13T22:40:36Z" }));
    value.history.history_begins = "2026-10-13T22:40:36Z";
    return value;
  });
  const otherFacts = homeFacts(other, LIVE_CONTRACT);
  assert.deepEqual(otherFacts.coverage, { state: "counted", works: 1730, states: 1731, articles: 52617, builtOn: "2026-10-14", indexSha256: other.result.value.mounted.index_sha256 }, "22:40 UTC on 13 October is the 14th in Luxembourg");
  // The index is older than today, so both dates are shown (R13; the plan's item 3.4).
  assert.equal(otherFacts.today, "2026-10-15");
  // The V4 line (the mockup's): the texts and the dated versions, grouped as the language groups them.
  assert.ok(mainOf(renderLiveHomePage({ facts: otherFacts })).includes("Lex holds 1,730 texts in 1,731 dated versions."));
  for (const [observedAt, today] of [["2026-10-14T21:59:59.0000000Z", "2026-10-14"], ["2026-10-14T22:00:00.0000000Z", "2026-10-15"]]) {
    assert.equal(homeFacts(coverageAt(observedAt, () => structuredClone(other.result.value)), LIVE_CONTRACT).today, today, `${observedAt}: today in Luxembourg`);
  }

  // A log that records no build: the counts, and no date.
  const undated = homeFacts(coverageAt("2026-10-15T10:00:00.0000000Z", (value) => ({
    ...value,
    history: { log_records_builds: false, snapshots_in_log: 0, history_begins: null, retention_policy: null, snapshots_with_text: [], snapshots_without_text: 0, note: value.history.note },
  })), LIVE_CONTRACT);
  assert.equal(undated.coverage.builtOn, null);
  assert.ok(mainOf(renderLiveHomePage({ facts: undated })).includes("Lex holds "), "the counts, with no date");

  // No corpus mounted: the coverage page's own sentence; an answer that cannot be read: no line.
  const refused = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.refusal?.code === "no_corpus_mounted")?.envelope;
  assert.ok(refused, "the census holds coverage's no-mount refusal");
  const unmounted = homeFacts(refused, LIVE_CONTRACT);
  assert.deepEqual(unmounted.coverage, { state: "unmounted" });
  assert.ok(mainOf(renderLiveHomePage({ facts: unmounted })).replaceAll("<!-- -->", "").includes('data-coverage-line="unmounted">This build has no index mounted. <a href="/en/coverage.html">'));
  const unreadable = homeFacts(coverageAt("2026-10-15T10:00:00.0000000Z", (value) => ({ ...value, totals: { ...value.totals, works: -1 } })), LIVE_CONTRACT);
  assert.deepEqual(unreadable.coverage, { state: "unavailable" });
  assert.ok(mainOf(renderLiveHomePage({ facts: unreadable })).includes('data-coverage-line="unavailable"><a href="/en/coverage.html">'), "no count, the coverage page alone");
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
  // The V4 Home and search results say it in the footer and in the dock (the brief: the column holds no such panel).
  for (const name of ["reading", "reading at a permalink", "the 404 page"]) {
    const main = mainOf(pages[name]);
    assert.ok(main.includes(said), `${name}: above the answer`);
    assert.ok(main.indexOf(said) < main.indexOf("data-live-answer"), `${name}: before the results or the text`);
  }
});

test("the V4 search results: the header's field alone, no form asking for an identifier, the texts by title, a reference opens its text", () => {
  const html = renderLiveSearchPage();
  const search = mainOf(html).replaceAll("<!-- -->", "");
  assert.ok(html.includes('data-layout="v4"') && html.includes('<aside class="lx-dock"'), "the V4 frame with the dock");
  // The dock's question box aside (the assistant's, `assistant-dock.test`).
  assert.equal((html.match(/<form\b(?![^>]*class="lx-dock-ask")/g) ?? []).length, 1, "the header's field alone");
  assert.ok(!search.includes(escaped(EN.form.workIdentifierOptional)) && !search.includes("<input"), "no field for an identifier, no form of its own");
  assert.ok(search.includes(`<h1 class="lx-find-heading">${escaped(EN.search.findHeading)}</h1><p class="lx-find-status">${escaped(EN.search.findIdle)}</p>`));
  assert.ok(search.includes('id="live-search-root" data-variant="search" data-address="kept"'));
  assert.equal(renderToString(liveSearchTree("search")), search.slice(search.indexOf('data-address="kept">') + 'data-address="kept">'.length, search.lastIndexOf("</div>")), "hydration changes nothing");
  const notFound = mainOf(renderLiveNotFoundPage());
  assert.ok(notFound.includes('data-variant="home"'), "the 404 page keeps the earlier search form");

  // Acceptance item 4: "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012", as the live API read it
  // on 15/11/2026, opens article 38 in the version of 16/04/2011: the text page's address, no model call.
  const reference = searchOutcome({ state: "success", envelope: EXACT_REFERENCE });
  assert.equal(reference.state, "opens");
  assert.equal(EXACT_REFERENCE.result.value.reference.phase, "exact_reference");
  assert.equal(openedHref(reference.view), textHref({ identifier: "/lu-legilux/loi-1997-07-27-n10", date: "2011-04-16", article: "art_38" }));
  // Other words: the texts by title, each once, each a link to its text page, never an identifier shown.
  const words = searchOutcome({ state: "success", envelope: envelopeOf("search", "the phrase as the site asks it") });
  assert.equal(words.state, "success");
  // The census's act has hits and no title held: it is not listed by its identifier (the page then says no text matches).
  assert.deepEqual(findRows(words.view), [], "no title, no row");
  assert.equal(openedHref(words.view), null, "words open nothing by themselves");
  // Acceptance item 1: the insurance contract law listed first for "contrat d'assurance" is one click from its text.
  const insurance = findRows({ ...words.view, workResolution: null, europeTitles: null, acts: [
    { workKey: "loi-1997-07-27-n10", title: "Version consolidée applicable au 01/01/2016 : Loi du 27 juillet 1997 sur le contrat d'assurance.", titleLanguage: "fra" },
    { workKey: "loi-2015-12-07-n1", title: null, titleLanguage: null },
    { workKey: "loi-1997-07-27-n10", title: "Loi du 27 juillet 1997 sur le contrat d'assurance", titleLanguage: "fra" },
  ] });
  assert.deepEqual(insurance.map((row) => [row.title, row.href]), [["Loi du 27 juillet 1997 sur le contrat d'assurance", textHref({ identifier: "/lu-legilux/loi-1997-07-27-n10" })]], "a text with no title held is not listed by its identifier");
});

/** A Luxembourg search answer from the census, asked at `date` (or none), as the results lay it out. */
function searchedAt(date, change = (view) => view) {
  const envelope = envelopeOf("search", "a phrase the held text carries");
  envelope.result.value.requested_date = date;
  // The population searched is the date's, as the platform counts it (the plan's item 4.3).
  envelope.result.value.searched = { ...envelope.result.value.searched, date, works_with_several_states_on_date: date === null ? null : 0 };
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  return renderToStaticMarkup(h(SearchAnswerView, { outcome: { ...outcome, view: change(outcome.view) }, onNextPage: () => {} })).replaceAll("<!-- -->", "");
}

test("a dated search says what this site holds was searched, in Luxembourg law, never that a version applies (review of #971)", () => {
  const html = searchedAt("2026-10-16");
  const dated = EN.search.dated.replace("{date}", "2026-10-16");
  assert.ok(html.includes(`data-searched-on="2026-10-16">${escaped(dated)}</p>`), html);
  // Index 9's rule (the review of #987, F5; reviewed French of 2026-10-19): the version whose publisher dates cover the date,
  // and an act whose version on the date this site holds no text of is not searched.
  assert.equal(EN.search.dated, "Only Luxembourg acts were searched. For each, the version whose publisher dates cover {date} was searched; an act with no such version, or for which this site does not hold that version's text, was not. An act held only as published is searched as published, and may have been amended since.");
  assert.equal(liveChrome("fr").search.dated, "Seuls des actes luxembourgeois ont été interrogés. Pour chacun, la version dont les dates de l’éditeur couvrent le {date} a été interrogée ; un acte sans une telle version, ou pour lequel ce site ne détient pas le texte de cette version, ne l’a pas été. Un acte détenu uniquement tel que publié est interrogé tel que publié, et peut avoir été modifié depuis.");
  // The version the interval rules select (SelectAt), which for an act held only as published is that act as published:
  // never "the version that applies", which on a population of acts held as first published claims too much.
  assert.doesNotMatch(dated, /\bthat applies\b|\bapplying on\b|\bin force\b/);
  for (const language of ["en", "fr"]) {
    for (const entry of ["dated", "undated", "noHitLuxembourg", "searchScope"]) assertTimeVocabulary(liveChrome(language).search[entry], `${language} search.${entry}`);
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

test("under the home page's box and the 404 page's, a line says a search by words covers Luxembourg law, an exact reference opens its act, Luxembourg or EU, and the search page searches within an EU act (review of #971; plan 4.2)", () => {
  // One action away under its summary (2026-11-13: the home page says one plain thing first, R51).
  const line = (html) => /<details class="search-scope" data-search-scope=""><summary>[^<]*<\/summary><p>([\s\S]*?)<\/p><\/details>/.exec(mainOf(html).replaceAll("<!-- -->", ""))?.[1] ?? null;
  for (const html of [renderLiveNotFoundPage()]) {
    assert.equal(line(html), 'A search by words covers Luxembourg law. An ELI, a CELEX number, a Mémorial reference, a permalink, or the citation of an article of an act, with or without a date, opens that act, Luxembourg or EU, when this site holds it. To search within an EU act, name it by its CELEX number on the <a href="/en/search.html">search page</a>.');
    const main = mainOf(html);
    assert.ok(main.indexOf("data-not-advice") < main.indexOf("data-search-scope") && main.indexOf("data-search-scope") < main.indexOf("data-live-answer"), "under R50's sentence, so the phone's first screen keeps the box and that sentence");
  }
  assert.equal(line(renderLiveSearchPage()), null, "the search page names a work itself");
  assert.equal(liveChrome("fr").search.searchScope, "La recherche par mots porte sur le droit luxembourgeois. Un ELI, un numéro CELEX, une référence au Mémorial, un permalien ou la citation d’un article d’un acte, avec ou sans date, ouvre cet acte, luxembourgeois ou de l’UE, lorsque ce site le détient. Pour interroger un acte de l’UE, désignez-le par son numéro CELEX sur la {link}.");
  assert.equal(liveChrome("fr").search.searchPage, "page de recherche");
});

test("the assistant's entry exists only while the server reports the assistant on, and the home page is byte-identical otherwise (R47, R52)", () => {
  // Off: a coverage answer without the capability flag (every recorded answer), the facts it gives and the page rendered
  // from them, are exactly what they were before the flag existed: no member, no link, no mention of the assistant.
  const off = homeFacts(COVERAGE, LIVE_CONTRACT);
  assert.equal(Object.hasOwn(off, "assistant"), false, "the facts carry no assistant member while it is off");
  for (const html of [rendered(COVERAGE), renderLiveHomePage()]) {
    assert.doesNotMatch(html, /data-home-assistant|\/en\/assistant/, "no link, button or route to the assistant while it is off");
  }
  assert.deepEqual(readHomeFactsValue(JSON.parse(JSON.stringify(off))), off, "the facts read back are the same facts");
  assert.equal(readHomeFactsValue({ ...off, assistant: "on" }), null, "a flag that is not exactly true is not the facts");

  // On: the server's coverage answer carries the flag, the facts carry `assistant: true`, and the page is the question box
  // (the owner's order of 2026-11-14, evening): its examples ask the assistant's page, and no list of links stands beside it.
  const on = homeFacts(coverageAt("2026-10-15T10:00:00.0000000Z", (value) => ({ ...value, assistant: { state: "on", operation_id: "ask", page: "assistant", preview: true } })), LIVE_CONTRACT);
  assert.equal(on.assistant, true);
  assert.deepEqual(readHomeFactsValue(JSON.parse(JSON.stringify(on))), on);
  const html = renderLiveHomePage({ facts: on });
  // The V4 Home (2026-11-15): the assistant is the dock's, so the column is the same either way.
  const rootOf = (page) => page.slice(page.indexOf('<div id="live-home-root">'), page.indexOf('<script type="application/json"'));
  assert.equal(rootOf(html), rootOf(renderLiveHomePage({ facts: { ...on, assistant: undefined } })), "the column does not change with the flag");
  // And the same facts without the flag render the page without it: the flag is the only difference.
  const { assistant, ...without } = on;
  assert.equal(assistant, true);
  assert.equal(mainOf(renderLiveHomePage({ facts: without })).includes("/en/assistant"), false);
});

test("outside its root and its facts, the home page rendered with the assistant on is the page as built, as the deploy probe holds it", () => {
  const on = homeFacts(coverageAt("2026-10-15T10:00:00.0000000Z", (value) => ({ ...value, assistant: { state: "on", operation_id: "ask", page: "assistant", preview: true } })), LIVE_CONTRACT);
  const outside = (html) => {
    const parts = homePageParts(html);
    const strip = (text) => withoutFreshness(text.replace(/<meta name="robots"[^>]*\/>/g, ""));
    return [strip(parts.before), withoutFreshness(parts.after)];
  };
  assert.deepEqual(outside(renderLiveHomePage({ facts: on })), outside(renderLiveHomePage()));
  assert.deepEqual(outside(rendered(COVERAGE)), outside(renderLiveHomePage()));
});

/** The recent changes as Home draws them, for the test above. */
function RecentChangesProbe({ rows }) {
  return h(RecentChanges, { rows });
}
