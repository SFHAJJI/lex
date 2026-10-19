// "Today" is the calendar date in Luxembourg (the plan's item 3.4; the specification of record's R13): "It is always
// printed, and written into the URL and the trace. When the index is older, both dates are shown." Held here: the date on
// both sides of each clock change, on every page that says it (the reading's form, the dossier, the search's population
// and the home page's coverage line, with today beside the index's date when the index is older); a readable address that
// keeps the date it was read at (`?date=`), asked at that date and said to be of another day; and the trace: every request
// that answers "as of today" names the date, which the answer echoes.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView, LIVE_CONTRACT, NotToday, SearchAnswerView, TodayHint, TodayLine, renderLiveHomePage, renderLiveReadingPage } from "../.react-build/app.mjs";
import { readingAddressShown, readingPinOf, readsAnotherDay, todayAt } from "../scripts/initial-answer.mjs";
import { dateOfQuery, sameAddressIn, sitePageFor } from "../scripts/permalinks.mjs";
import { indexOlderThanToday, searchOutcome } from "../scripts/live-search.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { homeFacts } from "../scripts/live-home.mjs";
import { luxembourgDateOrNull, todayInLuxembourg } from "../scripts/temporal.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope;
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const DOSSIER = envelopeOf("dossier", "the work, in the language it is held in");
const SEARCH = envelopeOf("search", "the phrase as the site asks it");
const COVERAGE = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
const STATE = READING.result.value.states[0];
const plain = (markup) => markup.replaceAll("<!-- -->", "");

/** The instants of the shared table around each clock change, in the API's seven-digit shape. */
const CHANGES = TABLE.instants.filter((row) => /summer time (starts|ends)/.test(row.case))
  .map((row) => ({ ...row, instant: row.instant.replace(/(?:\.(\d{1,7}))?Z$/, (_, digits = "") => `.${digits.padEnd(7, "0")}Z`) }));

/** An envelope answered at `instant`. */
const at = (envelope, instant, change = (value) => value) => {
  const answered = structuredClone(envelope);
  answered.context.freshness.observed_at = instant;
  if (answered.result !== null) answered.result.value = change(answered.result.value);
  return answered;
};

test("today on both sides of each clock change: the rule the pages read, and the day each answer was given on", () => {
  assert.ok(CHANGES.length >= 8, "the instants around the clock changes");
  for (const row of CHANGES) {
    assert.equal(luxembourgDateOrNull(row.instant), row.date, `${row.instant}: ${row.case}`);
    assert.equal(todayAt(row.instant), row.date);
    assert.equal(todayInLuxembourg(Date.parse(row.instant.replace(/(\.\d{3})\d+Z$/, "$1Z"))), row.date, `${row.instant}: this browser's clock`);
  }
  assert.equal(luxembourgDateOrNull(null), null);
  assert.equal(luxembourgDateOrNull("not an instant"), null);
  assert.equal(todayInLuxembourg(Number.NaN), null);
  assert.equal(todayInLuxembourg(8.64e15 + 1), null, "a clock past any date names none");
});

test("the dossier says today, the day its answer was given on, on both sides of each clock change", () => {
  for (const row of CHANGES) {
    const outcome = dossierOutcome({ state: "success", envelope: at(DOSSIER, row.instant) });
    assert.equal(outcome.state, "success", outcome.sentence);
    const markup = plain(renderToStaticMarkup(h(DossierAnswerView, { outcome })));
    assert.ok(markup.includes(`<p data-today="${row.date}">Today in Luxembourg: ${row.date}.</p>`), `${row.instant}: ${row.case}`);
    assert.ok(markup.indexOf("data-today") > markup.indexOf("data-history"), "under the count of the versions held");
  }
});

test("a reading's form says today beside its date, the answer's day, on both sides of each clock change", () => {
  for (const row of CHANGES) {
    // A readable address asked at today: the answer was asked for the day it was given on.
    const envelope = at(READING, row.instant, (value) => ({ ...value, requested_date: row.date }));
    const html = plain(renderLiveReadingPage({ initial: { envelope, path: "/en/lu-legilux/loi-1991-08-10-n3/fra" } }));
    assert.ok(html.includes(`<p id="reading-today" class="field-hint" data-today="${row.date}">Today in Luxembourg: ${row.date}.</p>`), `${row.instant}: ${row.case}`);
    assert.match(html, /aria-describedby="reading-today"/, "the date field names it");
    assert.ok(!html.includes("data-not-today"), `${row.instant}: read today`);
    // The same address read on the day before (an address that keeps a date): said, with today beside it.
    const before = at(READING, row.instant);
    const pin = readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra", before);
    assert.equal(readsAnotherDay(pin, readingOutcome({ state: "success", envelope: before })), STATE.applicability_date !== row.date);
  }
  // The lines themselves: none while today is not known.
  assert.equal(renderToStaticMarkup(h(TodayHint, { id: "x", today: null })), "");
  assert.equal(renderToStaticMarkup(h(TodayLine, { today: null })), "");
  assert.equal(plain(renderToStaticMarkup(h(TodayHint, { id: "compare-today", today: "2026-10-25" }))), '<p id="compare-today" class="field-hint" data-today="2026-10-25">Today in Luxembourg: 2026-10-25.</p>');
});

test("when the index is older than today, the search's population and the home page's coverage line say both dates", () => {
  assert.equal(indexOlderThanToday("2026-10-12", "2026-10-18"), true);
  assert.equal(indexOlderThanToday("2026-10-18", "2026-10-18"), false, "built today");
  assert.equal(indexOlderThanToday("2026-10-19", "2026-10-18"), false, "a build after today is not older");
  assert.equal(indexOlderThanToday(null, "2026-10-18"), false);
  assert.equal(indexOlderThanToday("2026-10-12", null), false);

  // Built at 21:59:59 UTC on 24 October 2026, the 24th in Luxembourg (summer time); answered either side of midnight there.
  for (const [observedAt, today] of [["2026-10-24T21:59:59.0000000Z", null], ["2026-10-24T22:00:00.0000000Z", "2026-10-25"], ["2026-10-25T23:00:00.0000000Z", "2026-10-26"]]) {
    const envelope = at(SEARCH, observedAt, (value) => ({ ...value, searched: { ...value.searched, index_built_at: "2026-10-24T21:59:59Z" } }));
    const outcome = searchOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    const line = /<p data-searched="\d+"[^>]*>([^<]+)</.exec(plain(renderToStaticMarkup(h(SearchAnswerView, { outcome }))))[1];
    assert.ok(line.includes("as of the index built on 2026-10-24") && (today === null ? line.endsWith("2026-10-24.") : line.endsWith(`2026-10-24; today in Luxembourg is ${today}.`)), `${observedAt}: ${line}`);

    const coverage = at(COVERAGE, observedAt, (value) => ({
      ...value,
      history: { ...value.history, snapshots_with_text: value.history.snapshots_with_text.map((snapshot) => ({ ...snapshot, built_at: "2026-10-24T21:59:59Z" })) },
    }));
    const facts = homeFacts(coverage, LIVE_CONTRACT);
    const said = /data-coverage-line="counted"[^>]*>([^<]+)</.exec(plain(renderLiveHomePage({ facts })))[1];
    assert.ok(today === null ? said.endsWith("as of the index built on 2026-10-24.") : said.endsWith(`as of the index built on 2026-10-24; today in Luxembourg is ${today}.`), `${observedAt}: ${said}`);
  }
});

test("a readable address keeps the date it was read at: asked at that date, said of another day, its links keeping it", () => {
  assert.equal(dateOfQuery("?date=2024-02-01"), "2024-02-01");
  assert.equal(dateOfQuery("?page=2&date=2024-02-01"), "2024-02-01");
  assert.equal(dateOfQuery("?date=2024-02-30"), null, "no calendar date: today");
  assert.equal(dateOfQuery("?date=2024-02-01&date=2025-01-01"), "2024-02-01", "the first");
  assert.equal(dateOfQuery(""), null);
  // Both servers ask the readable address at the date it keeps, whatever their clock says, and with no clock.
  for (const today of ["2026-10-24", "2026-10-25", null]) {
    assert.equal(sitePageFor("/fr/lu-legilux/loi-1991-08-10-n3/fra", { today, search: "?date=2024-02-01" }).parameters.date, "2024-02-01");
  }
  assert.equal(sitePageFor("/fr/lu-legilux/loi-1991-08-10-n3/fra", { today: null }), null, "no clock and no date kept: the page as built");
  // The page as built asks it too, before any answer (this browser's clock is not read for it).
  const pin = readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra/art_15", null, () => { throw new Error("the clock is not read"); }, "?date=2024-02-01");
  assert.deepEqual({ ...pin.request }, { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2024-02-01", language: "fra", article: "art_15" });
  assert.equal(readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra", null, () => "2026-10-24T22:00:00Z", "?date=x").request.date, "2026-10-25", "a date that is none: today");
  // A permalink names its own date: a date in its query is not read.
  assert.equal(readingPinOf(`/en${STATE.permalink}`, null, undefined, "?date=2020-01-01").request.date, STATE.applicability_date);

  // Read on another day: the line says the date read and today, with the act at today's date at the same address.
  const outcome = readingOutcome({ state: "success", envelope: READING });
  const readable = readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra/art_15", READING);
  assert.equal(readsAnotherDay(readable, outcome), true);
  assert.equal(
    plain(renderToStaticMarkup(h(NotToday, { outcome, pin: readable }))),
    `<p data-not-today="${STATE.applicability_date}">This page reads the act on ${STATE.applicability_date}, as this site holds it on that date; today in Luxembourg is 2026-09-18. <a href="/en/lu-legilux/loi-1991-08-10-n3/fra/art_15" data-read-today="">Read this act at today&#x27;s date</a></p>`,
  );
  assert.equal(readsAnotherDay(readingPinOf(`/en${STATE.permalink}/art_15`, READING), outcome), false, "a permalink names its date");
  assert.equal(readsAnotherDay(readable, { ...outcome, context: { ...outcome.context, freshness: { ...outcome.context.freshness, observed_at: "2024-02-01T10:00:00.0000000Z" } } }), false, "read today");
  // Its canonical: the readable address while the version shown is today's, else that version's permalink.
  assert.equal(readingAddressShown(outcome.view, "/lu-legilux/loi-1991-08-10-n3/fra/art_15", null, "2026-09-18"), "/lu-legilux/loi-1991-08-10-n3/fra/art_15");
  const superseded = readingOutcome({ state: "success", envelope: at(READING, READING.context.freshness.observed_at, (value) => ({ ...value, states: value.states.map((state) => ({ ...state, next_applicability_date: "2025-03-07" })) })) });
  assert.equal(readingAddressShown(superseded.view, "/lu-legilux/loi-1991-08-10-n3/fra/art_15", null, "2026-09-18"), `${STATE.permalink}/art_15`);
  assert.equal(readingAddressShown(superseded.view, "/lu-legilux/loi-1991-08-10-n3/fra", null, "2026-09-18"), STATE.permalink);
  assert.equal(readingAddressShown(superseded.view, "/lu-legilux/loi-1991-08-10-n3/fra/art_15", null, STATE.applicability_date), "/lu-legilux/loi-1991-08-10-n3/fra/art_15", "read today: the readable address");

  // The language menu keeps the date and the page a reading names, and the fragment; nothing else of the query.
  assert.equal(sameAddressIn("fr", { pathname: "/en/lu-legilux/loi-1991-08-10-n3/fra", search: "?from=card&page=2&date=2024-02-01", hash: "" }), "/fr/lu-legilux/loi-1991-08-10-n3/fra?page=2&date=2024-02-01");
  assert.equal(sameAddressIn("fr", { pathname: `/en${STATE.permalink}`, search: "?date=2024-02-01&page=3", hash: "#x" }), `/fr${STATE.permalink}?page=3#x`, "a permalink keeps no date");
  assert.equal(sameAddressIn("en", { pathname: "/fr/search.html", search: "?page=2&q=x", hash: "#q=cong%C3%A9&lang=fra" }), "/en/search.html#q=cong%C3%A9&lang=fra", "the search after #, never a query");
  assert.equal(sameAddressIn("en", { pathname: "/fr/", search: "", hash: "" }), "/en/");
});

test("the trace: every request that answers as of today names its date, and the answer echoes it", () => {
  // A readable address, asked by both servers and by the page as built: the date is in the request, never left to the API.
  const page = sitePageFor("/en/lu-legilux/loi-1991-08-10-n3/fra", { today: "2026-10-25" });
  assert.equal(page.parameters.date, "2026-10-25");
  assert.equal(readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra", null, () => "2026-10-24T22:00:00Z").request.date, "2026-10-25");
  // The answer echoes the date it was asked for, which the page names as the date read.
  assert.equal(readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra", READING).request.date, READING.result.value.requested_date);
  // The home page's search names today too (its date field starts at it; `live-search.test.mjs` holds the body).
  assert.match(renderLiveHomePage({ facts: homeFacts(at(COVERAGE, "2026-10-24T22:00:00.0000000Z"), LIVE_CONTRACT) }), /data-search-date="" aria-describedby="search-today" value="2026-10-25"/);
});
