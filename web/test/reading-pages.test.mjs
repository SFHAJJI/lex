// Reading by page (the plan's item 3.7; R73): a version read one page at a time, held to the census envelopes the real
// handler sent for a long act of synthetic text (60 articles, two pages; the first with its contents), and to the census's
// one-page reading, which is laid out as before. What the page lays out (its pages, its contents, the links before and
// after), what is put together from pages (the next articles added in place, and an export of a whole act, which keeps
// every article and its evidence, byte for byte), and what a page names as its own address.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { ExportAnswerView, ReadingAnswerView, renderLiveReadingPage } from "../.react-build/app.mjs";
import { syntheticReading } from "../scripts/reading-weight.mjs";
import { pageNotHeld, readingOutcome } from "../scripts/live-reading.mjs";
import { readingAddressShown, readingPinOf } from "../scripts/initial-answer.mjs";
import { assembleReading, hasNextPage, mergeReadingPage, nextPageRequest, pageAddress, pagesShown, readNextPage, showsWholeVersions, viewOf, withoutStopped } from "../scripts/reading-pages.mjs";
import { asOnePage, cutIntoPages } from "../scripts/reading-page-samples.mjs";
import { composeExport, exportCsv, exportJson } from "../scripts/export-build.mjs";
import { exportPdf } from "../scripts/export-pdf.mjs";
import { readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { pageOfQuery, readingRequestFor, parseSitePath, sitePageFor } from "../scripts/permalinks.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "evidence_bundle" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the evidence_bundle envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const FIRST = envelopeOf("a long act's state at its permalink, with its contents");
const SECOND = envelopeOf("the same long act's second page");
const ONE_PAGE = envelopeOf("a state's permalink at its own date");
const NOT_HELD = envelopeOf("a state's permalink and an article it does not hold");
const PAST_LAST = envelopeOf("the same long act's third page, past its last");
const LATER_NOT_HELD = envelopeOf("an EU wording's permalink whose next wording's text is not held");
const OBSERVED = "2026-10-17T08:00:00.0000000Z";
const viewFrom = (envelope) => {
  const outcome = readingOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  return outcome.view;
};
const markupOf = (outcome) => renderToStaticMarkup(h(ReadingAnswerView, { outcome })).replaceAll("<!-- -->", "");

test("a long act's pages read: the page served, the index of its pages, the contents, and each article's position", () => {
  const view = viewFrom(FIRST);
  const [state] = view.states;
  assert.deepEqual({ ...state.page }, {
    number: 1, pages: 2, firstPosition: 1, lastPosition: 50, articlesInVersion: 60, quotedInVersion: 60, withoutTextInVersion: 0, validityConflictsInVersion: 0,
  });
  assert.deepEqual(state.pages.map((row) => [row.number, row.firstPublisherId, row.lastPublisherId, row.articles]), [[1, "art_1er", "art_50", 50], [2, "art_51", "art_60", 10]]);
  assert.equal(state.contents.length, 60, "every article's place, no text");
  assert.deepEqual(state.contents.slice(48, 52).map((row) => [row.position, row.publisherId, row.page]), [[49, "art_49", 1], [50, "art_50", 1], [51, "art_51", 2], [52, "art_52", 2]]);
  assert.deepEqual(state.articles.map((article) => article.position), Array.from({ length: 50 }, (_, index) => index + 1));
  assert.equal(view.requestedPage, null);
  assert.match(view.pageRule, /at most 50 of them and at most 98,304 bytes/);
  assert.equal(hasNextPage(state), true);
  assert.deepEqual(pagesShown(state), { first: 1, last: 1 });
  const second = viewFrom(SECOND).states[0];
  assert.equal(second.page.number, 2);
  assert.equal(second.contents, null, "no contents unless asked");
  assert.equal(hasNextPage(second), false);
});

test("a long act's page lays out its pages: which are shown, the contents by page, and the links before and after", () => {
  const first = markupOf(readingOutcome({ state: "success", envelope: FIRST }));
  const permalink = FIRST.result.value.states[0].permalink;
  assert.ok(first.includes('<p data-counts="">60 articles quoted, 0 held without text'), "the state's own counts, not the page's");
  assert.ok(first.includes('<details data-contents="2"><summary>Contents</summary>'), "the contents, by page");
  assert.ok(first.includes(`<a href="/en${permalink}" aria-current="page">Page 1: art_1er to art_50</a>`), "the page shown, marked");
  assert.ok(first.includes(`<a href="/en${permalink}?page=2">Page 2: art_51 to art_60</a>`));
  assert.ok(first.includes('<p data-pages-shown="1-1">Page 1 of 2: art_1er to art_50.</p>'));
  assert.ok(first.includes('<ol class="articles" aria-live="off">'), "a page added in place is not read out whole");
  assert.ok(first.includes('<li id="fra-art_1er" data-article="art_1er" value="1">'), "each article numbered by its place in the state");
  assert.ok(first.includes(`<nav data-pager="" aria-label="Pages"><a rel="next" href="/en${permalink}?page=2" data-next-page="2">Next articles</a></nav>`),
    "the next articles: a plain link to the next page's address, which a page with scripts loads in place");
  assert.ok(!first.includes("data-previous-page"), "nothing before the first page");
  assert.equal((first.match(/data-article="/g) ?? []).length, 50, "one page of articles, never the whole act");

  const second = markupOf(readingOutcome({ state: "success", envelope: SECOND }));
  assert.ok(second.includes(`<a rel="prev" href="/en${permalink}" data-previous-page="1">Previous articles</a>`), "the page before: the version's address");
  assert.ok(!second.includes("data-next-page"), "nothing after the last page");
  assert.ok(second.includes('<p data-pages-shown="2-2">Page 2 of 2: art_51 to art_60.</p>'));
  assert.ok(second.includes('<li id="fra-art_51" data-article="art_51" value="51">'));

  // A version of one page is laid out as before: no contents, no pager, no numbers of its own.
  const one = markupOf(readingOutcome({ state: "success", envelope: ONE_PAGE }));
  for (const absent of ["data-contents", "data-pager", "data-pages-shown", 'aria-live="off"', " value="]) assert.ok(!one.includes(absent), absent);
});

test("the next articles are added in place below the shown ones, in the state's order, and the pages run on", async () => {
  const view = viewFrom(FIRST);
  const [state] = view.states;
  const request = { identifier: state.permalink, date: state.applicabilityDate, article: "art_7" };
  assert.deepEqual({ ...nextPageRequest(request, view) }, { identifier: state.permalink, date: state.applicabilityDate, language: "fra", page: 2 }, "the same request at the next page, no article");
  const asked = [];
  const { view: merged, version } = await readNextPage({ view, version: state, request, ask: async (next) => { asked.push(next); return viewFrom(SECOND); } });
  assert.deepEqual(asked.map((next) => next.page), [2]);
  assert.equal(version.articles.length, 60);
  assert.deepEqual(version.articles.map((article) => article.position), Array.from({ length: 60 }, (_, index) => index + 1));
  assert.deepEqual(pagesShown(version), { first: 1, last: 2 });
  assert.equal(showsWholeVersions(merged), true);
  assert.equal(nextPageRequest(request, merged), null, "nothing more to ask");
  const markup = markupOf({ state: "success", view: merged });
  assert.ok(markup.includes('<p data-pages-shown="1-2">Pages 1 to 2 of 2: art_1er to art_60.</p>'));
  assert.equal((markup.match(/data-article="/g) ?? []).length, 60);
  assert.ok(!markup.includes("data-next-page"));

  assert.throws(() => mergeReadingPage(view, view), /is page 1, after page 1/, "the same page twice is not the next page");
  assert.throws(() => mergeReadingPage(view, viewFrom(ONE_PAGE)), /reads a version this reading does not/, "another version's page is not this reading's");
  await assert.rejects(readNextPage({ view, version: state, request, ask: async () => viewOf(readingOutcome({ state: "transport_failure", code: "request_schema_invalid" })) }), /not answered/);
});

test("an export of a whole act is put together from its pages and keeps every article and its evidence, byte for byte", async () => {
  // The census's long act, its two pages put together, against the same 60 articles read as one page.
  const whole = await assembleReading({ first: viewFrom(FIRST), request: { identifier: FIRST.result.value.requested_identifier, date: FIRST.result.value.requested_date }, ask: async () => viewFrom(SECOND) });
  assert.equal(showsWholeVersions(whole), true);
  const [onePage] = cutIntoPages(structuredClone(FIRST.result.value), 60).map((answer) => readEvidenceBundle(mergedAnswer(answer)));
  const exported = (view) => {
    const [state] = view.states;
    const pinned = [...state.articles, ...state.articlesWithoutText].map((article) => ({ stateSha256: state.stateSha256, publisherId: article.publisherId }));
    return composeExport({ view, pinned, observedAt: OBSERVED });
  };
  const fromPages = exported(whole);
  const fromOne = exported(onePage);
  assert.equal(fromPages.items.length, 60, "every article");
  assert.equal(exportJson(fromPages), exportJson(fromOne), "the JSON");
  assert.equal(exportCsv(fromPages), exportCsv(fromOne), "the CSV");
  assert.deepEqual(exportPdf(fromPages), exportPdf(fromOne), "the PDF");
  assert.ok(fromPages.items.every((item, index) => item.publisherId === (index === 0 ? "art_1er" : `art_${index + 1}`)), "in the state's order");
  assert.ok(fromPages.items.every((item) => /^[0-9a-f]{64}$/.test(item.textSha256) && item.citation.startsWith(FIRST.result.value.states[0].permalink)), "each with its evidence");

  // An act that fits one page, cut into three by hand and put together again, exports what its one page exports.
  const captured = ONE_PAGE.result.value;
  const pages = cutIntoPages(structuredClone(captured), 20).map((answer) => readEvidenceBundle(answer));
  assert.equal(pages.length, 3);
  const assembled = await assembleReading({ first: pages[0], request: { identifier: captured.requested_identifier, date: captured.requested_date }, ask: async (next) => pages[next.page - 1] });
  const direct = readEvidenceBundle(captured);
  for (const write of [exportJson, exportCsv]) assert.equal(write(exported(assembled)), write(exported(direct)), write.name);
  assert.deepEqual(exportPdf(exported(assembled)), exportPdf(exported(direct)), "exportPdf");
  // Read from its one page, the act's reading is its whole: nothing is asked.
  assert.equal(await assembleReading({ first: direct, request: { identifier: captured.requested_identifier, date: captured.requested_date }, ask: async () => assert.fail("nothing to ask") }), direct);
});

/** One of `cutIntoPages`' answers whose one page holds every article: the census's first page with the second's articles. */
function mergedAnswer(answer) {
  const copy = structuredClone(answer);
  const state = copy.states[0];
  state.articles = [...FIRST.result.value.states[0].articles, ...SECOND.result.value.states[0].articles];
  state.page = { ...state.page, number: 1, pages: 1, first_position: 1, last_position: 60 };
  state.pages = [{ number: 1, first_publisher_id: "art_1er", last_publisher_id: "art_60", articles: 60 }];
  state.contents = null;
  return copy;
}

test("the export composer pins from the page shown, with the control that pins every article of the version", () => {
  const outcome = readingOutcome({ state: "success", envelope: FIRST });
  const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {}, onPinAll: () => {} })).replaceAll("<!-- -->", "");
  assert.ok(markup.includes('<p data-pages-shown="1-1">Page 1 of 2: art_1er to art_50.</p>'));
  assert.ok(markup.includes(`<button type="button" data-pin-all="${outcome.view.states[0].stateSha256}">Pin every article</button>`));
  const reading = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {}, onPinAll: () => {}, assembling: { [outcome.view.states[0].stateSha256]: { state: "loading", done: 1, pages: 2 } } }));
  assert.ok(reading.includes("Reading every page: 1 of 2 read."));
  assert.ok(reading.includes("disabled"), "one reading at a time");
});

test("a page names its own address: a page after the first with its page, the cited article's page with the article", () => {
  const second = viewFrom(SECOND);
  const permalink = second.states[0].permalink;
  assert.equal(readingAddressShown(second, permalink), `${permalink}?page=2`, "page 2 at the version's permalink");
  assert.equal(readingAddressShown(second, `${permalink}/art_55`), `${permalink}/art_55`, "the cited article's address names the article, not the page");
  assert.equal(readingAddressShown(viewFrom(FIRST), permalink), permalink, "page 1 is the address with no page");
  assert.equal(readingAddressShown(null, `${permalink}/art_99`, { code: "anchor_not_in_version", payload: NOT_HELD.refusal.helpful_payload }), permalink, "an article the version does not hold: the version's permalink");
  assert.equal(pageAddress(`${permalink}/art_55`, 2), `${permalink}?page=2`);
  assert.equal(pageAddress(permalink, 1), permalink);
  // The query's page, the article's page.
  for (const [search, page] of [["?page=2", 2], ["?page=1", null], ["?page=0", null], ["?page=02", null], ["?from=card&page=40", 40], ["?page=100001", null], ["", null], ["?pages=3", null]]) {
    assert.equal(pageOfQuery(search), page, search);
  }
  assert.deepEqual({ ...readingRequestFor(parseSitePath(`/fr${permalink}/art_55`), null, 3) }, { identifier: permalink, date: second.states[0].applicabilityDate, language: "fra", article: "art_55" }, "an article names its page");
  assert.deepEqual({ ...sitePageFor(`/fr${permalink}`, { search: "?page=2" }).parameters }, { identifier: permalink, date: second.states[0].applicabilityDate, language: "fra", page: 2 });
});

test("an article the version does not hold is refused with the ids nearest it, and the page links the whole act after the card", async () => {
  const { ArticleNotHeld, PinnedLead } = await import("../.react-build/app.mjs");
  const { readingPinOf } = await import("../scripts/initial-answer.mjs");
  assert.equal(NOT_HELD.refusal.code, "anchor_not_in_version");
  assert.ok(NOT_HELD.refusal.helpful_payload.nearest_anchors.includes("art_15"));
  const outcome = readingOutcome({ state: "refusal", envelope: NOT_HELD });
  assert.equal(outcome.card, true, "the card shows the platform's payload");
  assert.equal(outcome.sentence, "This index holds no article with that id in this version.", "the index's claim, never the version's (the French review of 2026-10-18)");
  const permalink = ONE_PAGE.result.value.states[0].permalink;
  const pin = readingPinOf(`/en${permalink}/art_15-9`, NOT_HELD);
  assert.equal(renderToStaticMarkup(h(PinnedLead, { outcome, pin })), "", "nothing before the card");
  assert.equal(renderToStaticMarkup(h(ArticleNotHeld, { outcome, pin })), `<p data-article-not-held=""><a href="/en${permalink}">The whole act at this version</a></p>`);
});

test("the speed check of the stand-in for the codes: the first page of 3,000 articles is a small fraction of the whole (R73)", () => {
  // The page budget (the plan's item 3.7; STATUS-WEB.md): a reading page weighs at most 400,000 bytes, about 3 per cent of the
  // 12.9 MB a whole reading of 3,000 articles weighed, whatever the version's length; the measured page is about 260 KB.
  const BUDGET = 400_000;
  const envelope = syntheticReading(census, 3000, "page");
  const [state] = envelope.result.value.states;
  assert.equal(state.page.articles_in_version, 3000);
  assert.equal(state.page.pages, 60);
  const started = performance.now();
  const html = renderLiveReadingPage({ initial: { envelope, path: `/fr${state.permalink}` } });
  const elapsed = performance.now() - started;
  const bytes = Buffer.byteLength(html, "utf8");
  assert.ok(bytes < BUDGET, `the first page of 3,000 articles weighs ${bytes} bytes, over its budget of ${BUDGET}`);
  assert.equal((html.match(/ data-article="/g) ?? []).length, 50, "one page of articles");
  assert.ok(html.replaceAll("<!-- -->", "").includes("Page 1 of 60: art_1er to art_50."));
  assert.ok(elapsed < 2_000, `rendered in ${Math.round(elapsed)} ms`);
});

test("a page of one article names it once, in the contents and in the pages shown (the French review of 2026-10-18)", () => {
  const whole = mergedAnswer(cutIntoPages(structuredClone(FIRST.result.value), 60)[0]);
  const [, last] = cutIntoPages(whole, 59);
  const markup = markupOf(readingOutcome({ state: "success", envelope: { ...structuredClone(FIRST), result: { ...FIRST.result, value: last } } }));
  assert.ok(markup.includes('<p data-pages-shown="2-2">Page 2 of 2: art_60.</p>'), markup.slice(0, 400));
  assert.ok(markup.includes(">Page 2: art_60</a>"));
  assert.ok(markup.includes(">Page 1: art_1er to art_59</a>"));
});

test("a page of today's version may open to search engines as its first page may, and names itself in the page's language", () => {
  // The census's long act: its one state applies from 2024-02-01 with none after, so it is the version its act's readable
  // address shows on the census's day (the plan's item 3.6: its canonical is that readable address, and the switch may open it).
  const permalink = SECOND.result.value.states[0].permalink;
  const today = "2026-09-17";
  assert.equal(readingAddressShown(viewFrom(SECOND), permalink, null, today), "/lu-legilux/loi-1991-08-10-n3/fra?page=2", "today's version's second page: the readable address, with its page");
  assert.equal(readingAddressShown(viewFrom(FIRST), permalink, null, today), "/lu-legilux/loi-1991-08-10-n3/fra", "its first page: the readable address alone");
  assert.equal(readingAddressShown(viewFrom(SECOND), `${permalink}/art_55`, null, today), "/lu-legilux/loi-1991-08-10-n3/fra/art_55", "an article: its readable address, no page");
  const html = renderLiveReadingPage({ initial: { envelope: SECOND, path: `/en${permalink}` } });
  const head = html.slice(0, html.indexOf("</head>"));
  assert.ok(head.includes('<link rel="canonical" href="https://law.soufien.lu/en/lu-legilux/loi-1991-08-10-n3/fra?page=2"/>'), head);
  assert.ok(head.includes('data-search-engines="kept-out"'), "today's version: kept out until the switch opens it, as its first page is");
});

test("a page of a version that is not today's stays out of search engines, at its permalink with its page (the review of #978, F3)", () => {
  // The census's long act with a later state from 2025-01-01: on the census's day its second page is a superseded version's.
  const superseded = structuredClone(SECOND);
  superseded.result.value.states[0].next_applicability_date = "2025-01-01";
  const permalink = superseded.result.value.states[0].permalink;
  assert.equal(readingAddressShown(viewFrom(superseded), permalink, null, "2026-09-17"), `${permalink}?page=2`, "its own permalink, with its page");
  const lu = renderLiveReadingPage({ initial: { envelope: superseded, path: `/en${permalink}` } });
  const luHead = lu.slice(0, lu.indexOf("</head>"));
  assert.ok(luHead.includes('<meta name="robots" content="noindex"/>'), luHead);
  assert.ok(!luHead.includes("data-search-engines"), "never opened by the switch");
  assert.ok(luHead.includes(`<link rel="canonical" href="https://law.soufien.lu/en${permalink}?page=2"/>`), luHead);

  // The census's EU wording, followed by a later one from 2024-01-01: its 44 provisions cut into pages of 20, the second.
  const wordingAnswer = structuredClone(LATER_NOT_HELD.result.value);
  asOnePage(wordingAnswer.wordings[0], { headings: true });
  const second = cutIntoPages(wordingAnswer, 20)[1];
  const envelope = { ...structuredClone(LATER_NOT_HELD), result: { ...LATER_NOT_HELD.result, value: second } };
  const wordingPermalink = second.wordings[0].permalink;
  assert.equal(readingAddressShown(viewFrom(envelope), wordingPermalink, null, "2026-09-17"), `${wordingPermalink}?page=2`);
  const eu = renderLiveReadingPage({ initial: { envelope, path: `/fr${wordingPermalink}` } });
  const euHead = eu.slice(0, eu.indexOf("</head>"));
  assert.ok(euHead.includes('<meta name="robots" content="noindex"/>'), euHead);
  assert.ok(!euHead.includes("data-search-engines"), "a superseded wording's page is never opened by the switch");
});

test("a contents row on another page than the index puts it on is refused (the review of #978, F3)", () => {
  for (const [at, page, expected] of [[50, 1, /contents\[50\] is on page 1, and the index puts it on page 2/], [0, 2, /contents\[0\] is on page 2, and the index puts it on page 1/]]) {
    const answer = structuredClone(FIRST.result.value);
    answer.states[0].contents[at].page = page;
    assert.throws(() => readEvidenceBundle(answer), expected);
  }
});

test("a page past the version's last is refused with its pages: a sentence, its first and last pages, and no search engine (the review of #978, F2)", async () => {
  const { ArticleNotHeld, PageNotHeld } = await import("../.react-build/app.mjs");
  assert.equal(PAST_LAST.refusal.code, "anchor_not_in_version");
  const payload = PAST_LAST.refusal.helpful_payload;
  assert.deepEqual([payload.requested_anchor, payload.nearest_anchors, payload.requested_page, payload.pages, payload.language], ["page=3", ["page=1", "page=2"], 3, 2, "fra"]);
  assert.deepEqual({ ...pageNotHeld(PAST_LAST.refusal.code, payload) }, { asked: 3, pages: 2, language: "fra" });
  assert.equal(pageNotHeld(NOT_HELD.refusal.code, NOT_HELD.refusal.helpful_payload), null, "an article not held is not a page");

  const outcome = readingOutcome({ state: "refusal", envelope: PAST_LAST });
  assert.equal(outcome.card, false, "no card: its note is about provisions, and a page is none");
  assert.equal(outcome.sentence, "This version is read here on 2 pages, so there is no page 3.");
  assert.equal(readingOutcome({ state: "refusal", envelope: { ...PAST_LAST, context: { ...PAST_LAST.context, publisher: "eu-eurlex" } } }).sentence,
    "This wording is read here on 2 pages, so there is no page 3.");
  const markup = markupOf(outcome);
  assert.ok(markup.includes("This version is read here on 2 pages, so there is no page 3."), markup);
  assert.ok(!markup.includes("full-text search"), "not the provision's note");

  const permalink = SECOND.result.value.states[0].permalink;
  const pin = readingPinOf(`/en${permalink}`, PAST_LAST);
  assert.equal(pin.request.page, 3, "the page asks again the page it was refused");
  assert.equal(renderToStaticMarkup(h(ArticleNotHeld, { outcome, pin })), "", "no whole act after a page");
  assert.equal(renderToStaticMarkup(h(PageNotHeld, { outcome, pin })).replaceAll("<!-- -->", ""),
    `<p data-page-not-held="2"><a href="/en${permalink}" data-first-page="">First page</a> <a href="/en${permalink}?page=2" data-last-page="2">Last page (page 2)</a></p>`);
  const readable = readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra", PAST_LAST);
  assert.ok(renderToStaticMarkup(h(PageNotHeld, { outcome, pin: readable })).includes('<a href="/en/lu-legilux/loi-1991-08-10-n3/fra?page=2" data-last-page="2">'),
    "at the act's readable address, its pages");
  assert.equal(renderToStaticMarkup(h(PageNotHeld, { outcome: readingOutcome({ state: "refusal", envelope: NOT_HELD }), pin })), "", "nothing after an article not held");

  // The page the server renders: the sentence and the links, never indexed, and never the malformed request's panel.
  const html = renderLiveReadingPage({ initial: { envelope: PAST_LAST, path: `/en${permalink}` } }).replaceAll("<!-- -->", "");
  const head = html.slice(0, html.indexOf("</head>"));
  assert.ok(head.includes('<meta name="robots" content="noindex"/>') && !head.includes("data-search-engines"), head);
  assert.ok(html.includes('data-answer-state="refusal"'));
  assert.ok(html.includes(`<a href="/en${permalink}?page=2" data-last-page="2">Last page (page 2)</a>`));
  assert.ok(!html.includes("request_schema_invalid"));
});

test("at an act's readable address a page's links go to that address's pages, not to the permalink's (the review of #978)", () => {
  // Read today: the answer given on the day it was asked for, as a readable address with no date of its own asks it.
  const today = structuredClone(SECOND);
  today.context.freshness.observed_at = today.context.freshness.observed_at.replace(/^\d{4}-\d{2}-\d{2}/, today.result.value.requested_date);
  const html = renderLiveReadingPage({ initial: { envelope: today, path: "/en/lu-legilux/loi-1991-08-10-n3/fra" } }).replaceAll("<!-- -->", "");
  const permalink = SECOND.result.value.states[0].permalink;
  assert.ok(html.includes('<a rel="prev" href="/en/lu-legilux/loi-1991-08-10-n3/fra" data-previous-page="1">Previous articles</a>'), "the page before: the act's address");
  assert.ok(!html.includes(`href="/en${permalink}?page=`), "no page link at the permalink");
  // At a permalink, the permalink's pages, as before; and at a readable address read on another day (one that keeps a date,
  // the plan's item 3.4), the version's own pages, by its permalink, since the act's address shows today's version.
  for (const path of [`/en${permalink}`, "/en/lu-legilux/loi-1991-08-10-n3/fra"]) {
    const pinned = renderLiveReadingPage({ initial: { envelope: SECOND, path } }).replaceAll("<!-- -->", "");
    assert.ok(pinned.includes(`<a rel="prev" href="/en${permalink}" data-previous-page="1">Previous articles</a>`), path);
  }
});

test("a page asked in place for another version, stopped by the next one asked, goes back to its link (the review of #978)", () => {
  const loading = { state: "loading" };
  assert.deepEqual(withoutStopped({ a: loading, b: { state: "added" } }, "a", "b"), { b: { state: "added" } }, "the stopped one's state is dropped");
  assert.deepEqual(withoutStopped({ a: { state: "added" } }, "a", "b"), { a: { state: "added" } }, "a page that came in stays");
  assert.deepEqual(withoutStopped({ a: { state: "failed" } }, "a", "b"), { a: { state: "failed" } });
  const same = { a: loading };
  assert.equal(withoutStopped(same, "a", "a"), same, "the same version asked again is not stopped");
  assert.equal(withoutStopped(same, null, "b"), same, "nothing was asked before");
});
