// How current the law is and which commits stand behind an answer (the specification of record's R87 and R23; the plan's
// items 6.1 and 6.5): the envelope reader holds the freshness header and the commits to the platform's rules, a page reads
// the answer's facts and computes none of its own, the footer strip and the staleness banner say them (and "lag not yet
// measured" until a lag is measured), a page as built says the strip's as-built line (and a page that ships no script, that
// it carries no answer), and promotion's data-age check says why it refuses.

import assert from "node:assert/strict";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import {
  FRESHNESS_STRIP_ID, FreshnessStrip, LIVE_CONTRACT, STALENESS_BANNER_ID, StalenessBanner, renderLiveHomePage, renderLiveReadingPage,
  renderLiveSearchPage,
} from "../.react-build/app.mjs";
import { ANSWER_EVENT, FRESHNESS_CHANNELS, announceAnswer, freshnessFacts, freshnessKey, staleChannels } from "../scripts/freshness.mjs";
import { readV3Envelope } from "../scripts/v3-envelope.mjs";
import { countedEntry, fillCounted, fillText, liveChrome } from "../scripts/live-chrome.mjs";
import { homeFacts } from "../scripts/live-home.mjs";
import { dataAgeFailures, homePageFailures, withoutFreshness } from "../scripts/deploy-probe.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { LEGAL_PAGE_PATHS } from "../scripts/legal-pages.mjs";
import { REFERENCE_PAGE_PATHS } from "../scripts/reference-pages.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (operation, scenarioStart = "") => structuredClone(census.envelopes.find((entry) => entry.operation === operation && entry.envelope.result !== null && entry.scenario.startsWith(scenarioStart)).envelope);
const COVERAGE = envelopeOf("coverage");
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const EN = liveChrome("en").freshness;
const FR = liveChrome("fr").freshness;
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
/** The strip's last line in a language: its info control, the section of How to read Lex on freshness (R48, `GuideLink.jsx`). */
const guideNote = (language) => {
  const copy = liveChrome(language);
  const name = fillText(copy.reference.infoNamed, { section: copy.guide.sections.freshness.heading });
  return `<p data-guide-note=""><a class="info-control" href="/${language}/how-to-read.html#guide-freshness" aria-label="${escaped(name)}" data-guide-link="freshness">${escaped(copy.reference.info)}</a></p>`;
};

/**
 * The coverage answer as the population's mount would state it on 2026-11-06: Luxembourg collected on 2026-10-12 (24 whole
 * days before the answer), the EU on 2026-10-05 (32 days, past the 30-day maximum, the owner's answer to O11 of
 * 2026-10-19), with both commits.
 */
function stale() {
  const envelope = structuredClone(COVERAGE);
  envelope.context.freshness = {
    ...envelope.context.freshness,
    observed_at: "2026-11-06T09:00:00.0000000Z",
    index_built_at: "2026-10-12T18:17:05Z",
    stale: true,
    channels: [
      { publisher: "lu-legilux", newest_completed_observation: "2026-10-12T18:17:04Z", age_days: 24, stale: false, lag_days: null, lag_observations: 0 },
      { publisher: "eu-eurlex", newest_completed_observation: "2026-10-05T03:50:08Z", age_days: 32, stale: true, lag_days: null, lag_observations: 0 },
    ],
  };
  envelope.context.commits = { index_builder_source_commit: "1".repeat(40), serving_runtime_source_commit: "2".repeat(40) };
  return envelope;
}

test("every census envelope carries the freshness header and the commits, and the reader holds them to the platform's rules", () => {
  for (const entry of census.envelopes) {
    const { freshness, commits } = entry.envelope.context;
    assert.ok(Array.isArray(freshness.channels), entry.scenario);
    assert.equal(freshness.last_checked_against_publisher, null, `${entry.scenario}: no monthly re-check has run (R69)`);
    assert.ok(freshness.channels.every((channel) => channel.lag_days === null && channel.lag_observations === 0), `${entry.scenario}: no lag measured`);
    assert.ok(Object.hasOwn(commits, "index_builder_source_commit") && Object.hasOwn(commits, "serving_runtime_source_commit"), entry.scenario);
  }
  assert.equal(readV3Envelope(stale(), contract, { operation: "coverage" }).ok, true, readV3Envelope(stale(), contract).reason);

  const cases = [
    ["a stale flag its channels do not give", (e) => { e.context.freshness.stale = false; }, /stale does not match its channels/],
    ["an age with no completion", (e) => { e.context.freshness.channels[0].newest_completed_observation = null; }, /age does not match its completion/],
    ["a completion with no age", (e) => { e.context.freshness.channels[0].age_days = null; }, /age does not match its completion/],
    ["a lag range stated", (e) => { e.context.freshness.channels[1].lag_days = { min: 1, max: 3 }; }, /a lag is stated, and none is measured/],
    ["observations behind a lag", (e) => { e.context.freshness.channels[1].lag_observations = 4; }, /a lag is stated, and none is measured/],
    ["a re-check named", (e) => { e.context.freshness.last_checked_against_publisher = "2026-10-17"; }, /re-check is named/],
    ["the channels out of order", (e) => { e.context.freshness.channels.reverse(); }, /Luxembourg first/],
    ["a channel twice", (e) => { e.context.freshness.channels[1].publisher = "lu-legilux"; }, /Luxembourg first/],
    ["an unknown channel", (e) => { e.context.freshness.channels[1].publisher = "de-bgbl"; }, /unknown publisher/],
    ["a build time in the answer's form", (e) => { e.context.freshness.index_built_at = "2026-10-12T18:17:05.0000000Z"; }, /index_built_at/],
    ["a maximum of no days", (e) => { e.context.freshness.maximum_data_age_days = 0; }, /maximum_data_age_days/],
    ["a short commit", (e) => { e.context.commits.index_builder_source_commit = "1".repeat(12); }, /full source commit/],
    ["a commit member too many", (e) => { e.context.commits.corpus_commit = null; }, /the commits does not have the exact closed member set/],
    ["a channel member too many", (e) => { e.context.freshness.channels[0].lag_range = null; }, /a freshness channel does not have the exact closed member set/],
    ["no commits", (e) => { delete e.context.commits; }, /the context does not have the exact closed member set/],
  ];
  for (const [what, mutate, reason] of cases) {
    const envelope = stale();
    mutate(envelope);
    const read = readV3Envelope(envelope, contract, { operation: "coverage" });
    assert.equal(read.ok, false, `${what} was read`);
    assert.match(read.reason, reason, what);
  }
});

test("a page reads the answer's facts and computes none of its own", () => {
  const facts = freshnessFacts(stale());
  assert.deepEqual(JSON.parse(JSON.stringify(facts)), {
    today: "2026-11-06",
    builtOn: "2026-10-12",
    maximumAgeDays: 30,
    stale: true,
    channels: [
      { publisher: "lu-legilux", completedOn: "2026-10-12", ageDays: 24, stale: false },
      { publisher: "eu-eurlex", completedOn: "2026-10-05", ageDays: 32, stale: true },
    ],
    lagMeasured: false,
    indexCommit: "1".repeat(40),
    serviceCommit: "2".repeat(40),
  });
  assert.deepEqual(staleChannels(facts).map((channel) => channel.publisher), ["eu-eurlex"]);
  assert.deepEqual(staleChannels(null), []);
  assert.equal(freshnessKey(null), "as-built");
  assert.notEqual(freshnessKey(facts), freshnessKey(freshnessFacts(COVERAGE)));
  assert.deepEqual(FRESHNESS_CHANNELS, ["lu-legilux", "eu-eurlex"]);

  // A date is the server's, in Luxembourg: a build at 23:30 UTC in October is the next day there.
  const late = stale();
  late.context.freshness.index_built_at = "2026-10-12T23:30:00Z";
  assert.equal(freshnessFacts(late).builtOn, "2026-10-13");

  // The census's coverage: its log's build date, a Luxembourg channel whose completion its mount does not state, and the
  // samples' fixed serving commit.
  const census = freshnessFacts(COVERAGE);
  assert.equal(census.builtOn, COVERAGE.context.freshness.index_built_at.slice(0, 10));
  assert.deepEqual(census.channels.map((channel) => [channel.publisher, channel.completedOn, channel.ageDays, channel.stale]), [["lu-legilux", null, null, null]]);
  assert.equal(census.stale, false);
  assert.equal(census.indexCommit, null);
  assert.equal(census.serviceCommit, "e".repeat(40));

  // Nothing a page can read is no facts: no freshness, a channel it does not know, an age with no completion.
  assert.equal(freshnessFacts(null), null);
  assert.equal(freshnessFacts({ context: {} }), null);
  const unknown = stale();
  unknown.context.freshness.channels[0].publisher = "de-bgbl";
  assert.equal(freshnessFacts(unknown), null);
  const ageless = stale();
  ageless.context.freshness.channels[0].newest_completed_observation = null;
  assert.equal(freshnessFacts(ageless), null);
});

test("the strip says the build date, each channel's last complete collection and its lag, and the two commits", () => {
  const html = renderToStaticMarkup(h(FreshnessStrip, { facts: freshnessFacts(stale()) }));
  assert.ok(html.startsWith(`<section class="freshness-strip" aria-label="${EN.heading}" data-freshness="answered" data-stale="true">`), html);
  assert.ok(html.includes("Index built on 2026-10-12."), html);
  assert.ok(html.includes(escaped("Luxembourg law, from Legilux: last complete collection ended on 2026-10-12, 24 full days before the server answered on 2026-11-06; lag not yet measured.")), html);
  assert.ok(html.includes(escaped("EU law, from the Publications Office of the European Union: last complete collection ended on 2026-10-05, 32 full days before the server answered on 2026-11-06; lag not yet measured.")), html);
  assert.ok(html.includes(`<dt>${EN.indexCommit}</dt><dd><code class="commit">${"1".repeat(40)}</code></dd>`), html);
  assert.ok(html.includes(`<dt>${EN.serviceCommit}</dt><dd><code class="commit">${"2".repeat(40)}</code></dd>`), html);
  assert.ok(!html.includes("<section", 1), "the strip holds no section inside it (the deploy probe sets it aside by its end)");
  assert.ok(html.endsWith(`${guideNote("en")}</section>`), "the strip ends with its info control (R48)");
  // "lag not yet measured" for every channel, and never a lag of its own.
  assert.equal(html.split("lag not yet measured").length - 1, 2);

  // The census's coverage: a completion the mount does not state, no index commit, and the as-built line before any answer.
  const census = renderToStaticMarkup(h(FreshnessStrip, { facts: freshnessFacts(COVERAGE) }));
  assert.ok(census.includes(escaped("Luxembourg law, from Legilux: when its last complete collection ended is not recorded with this index; lag not yet measured.")), census);
  assert.ok(census.includes(`<dt>${EN.indexCommit}</dt><dd>${EN.commitUnstated}</dd>`), census);
  assert.equal(EN.commitUnstated, "(not recorded)");
  assert.ok(!census.includes("data-stale=\"true\""));
  const asBuilt = renderToStaticMarkup(h(FreshnessStrip, {}));
  assert.equal(asBuilt, `<section class="freshness-strip" aria-label="${EN.heading}" data-freshness="as-built"><p>${escaped(EN.asBuilt)}</p>${guideNote("en")}</section>`);
  // A page that never asks the server says it carries no answer, never the as-built line's "once it has an answer" (the
  // review of #990); an answer's facts, had it any, would still be said.
  assert.equal(renderToStaticMarkup(h(FreshnessStrip, { answers: false })), `<section class="freshness-strip" aria-label="${EN.heading}" data-freshness="no-answer"><p>${escaped(EN.noAnswer)}</p>${guideNote("en")}</section>`);
  assert.ok(renderToStaticMarkup(h(FreshnessStrip, { facts: freshnessFacts(stale()), answers: false })).includes('data-freshness="answered"'));

  // An index that records no build date says so.
  const undated = stale();
  undated.context.freshness.index_built_at = null;
  assert.ok(renderToStaticMarkup(h(FreshnessStrip, { facts: freshnessFacts(undated) })).includes(EN.builtUndated));
});

test("the banner names each channel past the maximum, by the answer's maximum, and nothing when none is", () => {
  const html = renderToStaticMarkup(h(StalenessBanner, { facts: freshnessFacts(stale()) }));
  assert.ok(html.startsWith(`<aside class="staleness-banner" role="note" aria-label="${EN.staleHeading}" data-stale-channels="eu-eurlex"><details><summary><strong>${EN.staleHeading}</strong></summary>`), html);
  assert.ok(html.includes(escaped("EU law, from the Publications Office of the European Union: its last complete collection ended on 2026-10-05, more than 30 days before the server answered on 2026-11-06.")), html);
  assert.ok(!html.includes("Legilux"), "a channel within the maximum is not named");
  assert.equal(renderToStaticMarkup(h(StalenessBanner, { facts: freshnessFacts(COVERAGE) })), "", "no completion stated: nothing is said to be stale");
  assert.equal(renderToStaticMarkup(h(StalenessBanner, {})), "");

  // The maximum is the answer's, never a figure in the copy (R11).
  const longer = stale();
  longer.context.freshness.maximum_data_age_days = 12;
  assert.ok(renderToStaticMarkup(h(StalenessBanner, { facts: freshnessFacts(longer) })).includes("more than 12 days before the server answered"));
});

test("the French says each count by the language's plural rule, and no form says a date word of either publisher", () => {
  assert.equal(countedEntry(FR.collected, 0, "fr"), FR.collected.one, "0 jour");
  assert.equal(countedEntry(FR.collected, 1, "fr"), FR.collected.one);
  assert.equal(countedEntry(FR.collected, 2, "fr"), FR.collected.other);
  assert.equal(countedEntry(EN.collected, 0, "en"), EN.collected.other, "0 days");
  for (const [language, copy] of [["en", EN], ["fr", FR]]) {
    for (const count of [0, 1, 5, 13]) {
      const said = fillCounted(copy.collected, count, { channel: copy.channels["eu-eurlex"], date: "2026-10-05", today: "2026-10-18" }, language);
      assert.ok(said.includes(String(count)), said);
      assertTimeVocabulary(said, `${language} collected ${count}`);
    }
    assertTimeVocabulary(fillCounted(copy.stale, 30, { channel: copy.channels["lu-legilux"], date: "2026-10-05", today: "2026-10-18" }, language), `${language} stale`);
    for (const line of ["asBuilt", "noAnswer"]) assertTimeVocabulary(copy[line], `${language} ${line}`);
    assert.deepEqual(Object.keys(copy.channels), ["lu-legilux", "eu-eurlex"], language);
  }
});

test("a page that ships no script says its strip carries no answer, in English and in French; a page with a script keeps the as-built line (the review of #990)", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-freshness-"));
  try {
    // Tagged, so this build's intermediate bundles never collide with another test file's build.
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "freshness-no-answer" });
    const pages = [];
    for (const language of ["en", "fr"]) {
      for (const name of (await readdir(join(destination, language))).filter((file) => file.endsWith(".html"))) pages.push([`${language}/${name}`, language]);
      for (const folder of ["not-found", "maintenance"]) pages.push([`${folder}/${language}.html`, language]);
    }
    // The language pages German and Luxembourgish readers get, in English (`live-locale-page.jsx`).
    for (const name of (await readdir(destination)).filter((file) => /^locale-[a-z]+\.html$/.test(file))) pages.push([name, "en"]);
    const quiet = [];
    for (const [label, language] of pages) {
      const html = await readFile(join(destination, label), "utf8");
      const copy = liveChrome(language).freshness;
      const strip = html.slice(html.indexOf(`<div id="${FRESHNESS_STRIP_ID}"`), html.indexOf("</footer>"));
      const line = (said) => `<section class="freshness-strip" aria-label="${escaped(copy.heading)}" data-freshness="${said === copy.noAnswer ? "no-answer" : "as-built"}"><p>${escaped(said)}</p>${guideNote(language)}</section>`;
      if (/<script src="/.test(html)) {
        // A script that asks the server, and redraws the strip from each answer (`client.jsx`): the as-built line is true.
        assert.equal(strip, `<div id="${FRESHNESS_STRIP_ID}" data-freshness-key="as-built">${line(copy.asBuilt)}</div>`, label);
      } else {
        quiet.push(label);
        assert.equal(strip, `<div id="${FRESHNESS_STRIP_ID}" data-freshness-key="as-built">${line(copy.noAnswer)}</div>`, label);
        assert.ok(!html.includes("<form"), `${label}: asks nothing`);
      }
    }
    // The pages that never ask: the ten legal pages, the six reference pages (Get help, How to read Lex and Developers, in
    // each language), the maintenance page in each language and the two language pages.
    assert.deepEqual(quiet.sort(), [
      ...LEGAL_PAGE_PATHS.map((path) => path.slice(1)), ...REFERENCE_PAGE_PATHS.map((path) => path.slice(1)), "locale-de.html", "locale-lb.html", "maintenance/en.html", "maintenance/fr.html",
    ].sort());
    // And the 404 page, which offers the search and so may have an answer, keeps the as-built line.
    assert.ok(pages.some(([label]) => label === "not-found/fr.html") && !quiet.includes("not-found/fr.html"));
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("a page the server renders from an answer carries its strip, and its banner under the header when it is stale", () => {
  const home = renderLiveHomePage({ facts: homeFacts(stale(), LIVE_CONTRACT), freshness: freshnessFacts(stale()) });
  const header = home.indexOf("</header>");
  const banner = home.indexOf(`<div id="${STALENESS_BANNER_ID}"`);
  const main = home.indexOf("<main");
  assert.ok(header < banner && banner < main, "the banner stands between the header and the content");
  assert.ok(home.slice(banner, main).includes("staleness-banner"));
  const footer = home.slice(home.indexOf("<footer"));
  assert.ok(footer.includes(`<div id="${FRESHNESS_STRIP_ID}"`) && footer.includes('data-freshness="answered"'), "the strip, last in the footer");
  assert.ok(footer.indexOf("data-not-advice") < footer.indexOf(FRESHNESS_STRIP_ID), "after what Lex is not");

  // A law's page rendered from its own answer says that answer's facts; as built, the strip's as-built line and no banner.
  const reading = renderLiveReadingPage({ initial: { envelope: READING, path: `/en${READING.result.value.states[0].permalink}` } });
  assert.ok(reading.includes('data-freshness="answered"'));
  const built = renderLiveSearchPage();
  assert.ok(built.includes('data-freshness="as-built"') && !built.includes("staleness-banner"));
  assert.ok(built.includes(`<div id="${STALENESS_BANNER_ID}" data-freshness-key="as-built"></div>`), "an empty container the browser fills");

  // The deploy probe holds a rendered home page to the page as built outside its answer: the strip and the banner set aside.
  const asBuilt = renderLiveHomePage();
  assert.equal(withoutFreshness(asBuilt).includes("freshness-strip"), false);
  assert.equal(withoutFreshness(home).includes("staleness-banner"), false);
  assert.ok(withoutFreshness(home).includes("<!--freshness-->") && withoutFreshness(home).includes("<!--staleness-->"));
});

test("promotion refuses data past the maximum age or with no completion stated, and says which channel", () => {
  assert.deepEqual(dataAgeFailures(stale()), [
    "eu-eurlex: its newest completed collection ended at 2026-10-05T03:50:08Z, 32 days before the answer, past the maximum data age of 30 days",
  ]);
  const fresh = stale();
  fresh.context.freshness.channels[1] = { ...fresh.context.freshness.channels[1], newest_completed_observation: "2026-10-12T18:17:04Z", age_days: 24, stale: false };
  fresh.context.freshness.stale = false;
  assert.deepEqual(dataAgeFailures(fresh), []);
  assert.deepEqual(dataAgeFailures(COVERAGE), ["lu-legilux: the mount states no completed collection, so its data's age is not known to be within 30 days"]);
  assert.deepEqual(dataAgeFailures(census.envelopes.find((entry) => entry.envelope.refusal?.code === "no_corpus_mounted").envelope),
    ["the answer names no publisher channel, so the data's age is not known"]);
  assert.deepEqual(dataAgeFailures({ context: {} }), ["the answer carries no freshness header, so the data's age is not known"]);
  // homePageFailures is untouched by it: a stale revision's page is still the release's own.
  assert.equal(typeof homePageFailures, "function");
});

test("an answer is announced on the page's document, and nowhere outside a page", () => {
  const heard = [];
  const previous = globalThis.document;
  try {
    globalThis.document = { dispatchEvent: (event) => heard.push([event.type, event.detail]) };
    announceAnswer(COVERAGE);
    assert.deepEqual(heard, [[ANSWER_EVENT, COVERAGE]]);
  } finally {
    if (previous === undefined) delete globalThis.document;
    else globalThis.document = previous;
  }
  assert.doesNotThrow(() => announceAnswer(COVERAGE), "no document: nothing to announce on");
});
